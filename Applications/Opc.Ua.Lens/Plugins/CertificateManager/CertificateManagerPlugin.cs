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
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Security.Certificates;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Plugins.CertificateManager;

/// <summary>
/// Tab-hosted certificate-management plug-in.  Replaces the modal
/// <see cref="CertificateStoreDialog"/> + <see cref="CertificateTrustDialog"/>
/// pairing with a persistent workbench tab:
/// <list type="bullet">
///   <item>Left pane — tree of well-known cert stores (Application,
///   TrustedPeer, TrustedIssuer, Rejected) enumerated from the active
///   <see cref="ApplicationConfiguration.SecurityConfiguration"/>,
///   plus any user-added DirectoryStore roots.</item>
///   <item>Right pane — ListBox of certificates in the selected store
///   with Trust / Reject / Delete / Export / Import / View-details
///   context-menu actions.</item>
/// </list>
/// The plug-in works without an active OPC UA session — certificate
/// management is a pre-connect concern.  All store I/O is dispatched
/// against <see cref="CertificateStoreIdentifier.OpenStore(ITelemetryContext)"/>
/// using the telemetry context obtained from the host.
/// </summary>
internal sealed partial class CertificateManagerPlugin : ObservableObject, IPlugin
{
    private static readonly string[] s_derExportPatterns = ["*.cer", "*.crt", "*.der"];
    private static readonly string[] s_pemExportPatterns = ["*.pem"];
    private static readonly string[] s_certImportPatterns = ["*.cer", "*.crt", "*.der", "*.pem"];

    private static int s_nextNumber;

    private readonly PluginHost m_host;
    private readonly ILogger m_log;
    private CertificateManagerView? m_view;
    private ApplicationConfiguration? m_config;

    [ObservableProperty]
    private string m_title;

    [ObservableProperty]
    private bool m_isRenaming;

    [ObservableProperty]
    private string m_status = "● Loading certificate stores…";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private CertStoreNode? m_selectedStore;

    [ObservableProperty]
    private CertItemRow? m_selectedCertificate;

    /// <summary>Stores rendered in the left-hand TreeView.</summary>
    public ObservableCollection<CertStoreNode> Stores { get; } = new();

    /// <summary>Certificates in the currently-selected store.</summary>
    public ObservableCollection<CertItemRow> Certificates { get; } = new();

    public CertificateManagerPlugin(PluginHost host)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_log = host.Log;
        int n = Interlocked.Increment(ref s_nextNumber);
        m_title = string.Create(CultureInfo.InvariantCulture, $"Certificate Manager {n}");

        // Load stores in the background — no session required.
        _ = Dispatcher.UIThread.InvokeAsync(LoadStoresAsync);
    }

    // ----- IPlugin -----

    public PluginKind Kind => PluginKind.CertificateManager;

    Control? IPlugin.View => m_view ??= new CertificateManagerView { DataContext = this };
    Control? IPlugin.HeaderToolbar => null;

    public bool SupportsDuplicate => false;

    public void OnActivated() { }
    public void OnDeactivated() { }

    public IReadOnlyList<MenuItem> ContributeMenuItems()
    {
        var refresh = new MenuItem { Header = "_Refresh" };
        var addStore = new MenuItem { Header = "_Add Store…" };
        var openTrust = new MenuItem { Header = "_Open Trust Dialog…" };
        var viewDetails = new MenuItem { Header = "_View Details…" };
        var trustPeer = new MenuItem { Header = "Trust → _Peer" };
        var trustIssuer = new MenuItem { Header = "Trust → _Issuer" };
        var reject = new MenuItem { Header = "Re_ject" };
        var delete = new MenuItem { Header = "_Delete" };
        var export = new MenuItem { Header = "_Export…" };
        var import = new MenuItem { Header = "I_mport…" };

        refresh.Click += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        addStore.Click += async (_, _) => await AddStoreAsync().ConfigureAwait(true);
        openTrust.Click += async (_, _) => await OpenTrustDialogAsync().ConfigureAwait(true);
        viewDetails.Click += async (_, _) => await ViewDetailsAsync().ConfigureAwait(true);
        trustPeer.Click += async (_, _) => await TrustToPeerAsync().ConfigureAwait(true);
        trustIssuer.Click += async (_, _) => await TrustToIssuerAsync().ConfigureAwait(true);
        reject.Click += async (_, _) => await RejectAsync().ConfigureAwait(true);
        delete.Click += async (_, _) => await DeleteAsync().ConfigureAwait(true);
        export.Click += async (_, _) => await ExportAsync().ConfigureAwait(true);
        import.Click += async (_, _) => await ImportAsync().ConfigureAwait(true);

        return [refresh, addStore, openTrust, viewDetails,
                trustPeer, trustIssuer, reject, delete, export, import];
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    // ----- Property-changed hooks -----

    partial void OnSelectedStoreChanged(CertStoreNode? value)
    {
        _ = ReloadSelectedStoreAsync();
    }

    // ----- Store-tree loading -----

    private async Task LoadStoresAsync()
    {
        try
        {
            m_config ??= await m_host.Connection.GetConfigAsync().ConfigureAwait(true);
            SecurityConfiguration? sec = m_config.SecurityConfiguration;
            Stores.Clear();

            if (sec is null)
            {
                Status = "● No SecurityConfiguration available.";
                return;
            }

            // ApplicationCertificate exposes StorePath/StoreType but not
            // a direct OpenStore call; wrap it so we can list its certs.
            if (sec.ApplicationCertificate is { } appId
                && !string.IsNullOrEmpty(appId.StorePath))
            {
                CertificateStoreIdentifier id = string.IsNullOrEmpty(appId.StoreType)
                    ? new CertificateStoreIdentifier(appId.StorePath!, noPrivateKeys: false)
                    : new CertificateStoreIdentifier(appId.StorePath!, appId.StoreType!, noPrivateKeys: false);
                Stores.Add(new CertStoreNode(CertStoreRole.Application, "Application", id));
            }

            if (sec.TrustedPeerCertificates is { } peer
                && !string.IsNullOrEmpty(peer.StorePath))
            {
                Stores.Add(new CertStoreNode(CertStoreRole.TrustedPeer, "Trusted Peers", peer));
            }

            if (sec.TrustedIssuerCertificates is { } issuer
                && !string.IsNullOrEmpty(issuer.StorePath))
            {
                Stores.Add(new CertStoreNode(CertStoreRole.TrustedIssuer, "Trusted Issuers", issuer));
            }

            if (sec.RejectedCertificateStore is { } rej
                && !string.IsNullOrEmpty(rej.StorePath))
            {
                Stores.Add(new CertStoreNode(CertStoreRole.Rejected, "Rejected", rej));
            }

            Status = string.Format(CultureInfo.InvariantCulture,
                "● {0} store(s) — select one to enumerate certificates.", Stores.Count);

            if (SelectedStore is null && Stores.Count > 0)
            {
                SelectedStore = Stores[0];
            }
        }
        catch (Exception ex)
        {
            m_log.LogError(ex, "Certificate Manager tab {Title} LoadStores failed.", Title);
            Status = $"● Load stores failed: {ex.Message}";
        }
    }

    // ----- Cert-list loading -----

    private async Task ReloadSelectedStoreAsync()
    {
        Certificates.Clear();
        SelectedCertificate = null;
        if (SelectedStore is not { } node)
        {
            Status = "● No store selected.";
            return;
        }

        try
        {
            IReadOnlyList<X509Certificate2> certs = await EnumerateAsync(node).ConfigureAwait(true);
            foreach (X509Certificate2 c in certs)
            {
                Certificates.Add(CertItemRow.From(c));
            }
            Status = string.Format(CultureInfo.InvariantCulture,
                "● {0}: {1} certificate(s).", node.DisplayName, Certificates.Count);
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Certificate Manager tab {Title} Enumerate({Store}) failed.",
                Title, node.DisplayName);
            Status = $"● Enumerate {node.DisplayName} failed: {ex.Message}";
        }
    }

    private async Task<IReadOnlyList<X509Certificate2>> EnumerateAsync(
        CertStoreNode node, CancellationToken ct = default)
    {
        ICertificateStore? store = node.Identifier.OpenStore(m_host.Main.Telemetry);
        if (store is null)
        {
            return Array.Empty<X509Certificate2>();
        }
        try
        {
            CertificateCollection coll = await store.EnumerateAsync(ct).ConfigureAwait(true);
            var list = new List<X509Certificate2>(coll.Count);
            foreach (Certificate cert in coll)
            {
                list.Add(cert.AsX509Certificate2());
            }
            return list;
        }
        finally
        {
            store.Close();
            store.Dispose();
        }
    }

    private async Task<bool> AddToStoreAsync(
        CertStoreNode node, X509Certificate2 cert, CancellationToken ct = default)
    {
        ICertificateStore? store = node.Identifier.OpenStore(m_host.Main.Telemetry);
        if (store is null)
        {
            return false;
        }
        try
        {
            using Certificate wrapper = Certificate.From(cert);
            await store.AddAsync(wrapper, password: null, ct).ConfigureAwait(true);
            m_log.LogInformation("Added certificate {Thumbprint} ({Subject}) to {Store}.",
                cert.Thumbprint, cert.Subject, node.DisplayName);
            return true;
        }
        catch (Exception ex)
        {
            m_log.LogWarning(ex, "Add to {Store} failed.", node.DisplayName);
            return false;
        }
        finally
        {
            store.Close();
            store.Dispose();
        }
    }

    private async Task<bool> DeleteFromStoreAsync(
        CertStoreNode node, string thumbprint, CancellationToken ct = default)
    {
        ICertificateStore? store = node.Identifier.OpenStore(m_host.Main.Telemetry);
        if (store is null)
        {
            return false;
        }
        try
        {
            return await store.DeleteAsync(thumbprint, ct).ConfigureAwait(true);
        }
        finally
        {
            store.Close();
            store.Dispose();
        }
    }

    private CertStoreNode? FindStore(CertStoreRole role)
    {
        foreach (CertStoreNode n in Stores)
        {
            if (n.Role == role)
            {
                return n;
            }
        }
        return null;
    }

    // ----- Toolbar commands -----

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (SelectedStore is null)
        {
            await LoadStoresAsync().ConfigureAwait(true);
            return;
        }
        await ReloadSelectedStoreAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Prompt the user for a directory path and attach it as a custom
    /// <c>Directory</c> store to the tree.  Custom stores are kept in
    /// memory only — they are not persisted to the application config.
    /// </summary>
    [RelayCommand]
    public async Task AddStoreAsync()
    {
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider
                .OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Pick a DirectoryStore folder",
                    AllowMultiple = false
                }).ConfigureAwait(true);
            if (folders.Count == 0)
            {
                return;
            }
            string path = folders[0].Path.LocalPath;
            if (string.IsNullOrEmpty(path))
            {
                Status = "● Custom store path is empty.";
                return;
            }
            var id = new CertificateStoreIdentifier(path, CertificateStoreType.Directory, noPrivateKeys: true);
            string displayName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = path;
            }
            var node = new CertStoreNode(CertStoreRole.Custom, displayName, id);
            Stores.Add(node);
            SelectedStore = node;
            Status = $"● Added custom store {displayName}.";
        }
        catch (Exception ex)
        {
            Status = $"● Add store failed: {ex.Message}";
            m_log.LogWarning(ex, "Certificate Manager tab {Title} AddStore failed.", Title);
        }
    }

    /// <summary>
    /// Launch the legacy <see cref="CertificateStoreDialog"/> for
    /// backward compatibility.  Provides users who learned the modal
    /// flow a way to keep using it.
    /// </summary>
    [RelayCommand]
    public async Task OpenTrustDialogAsync()
    {
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }

        try
        {
            m_config ??= await m_host.Connection.GetConfigAsync().ConfigureAwait(true);
            var dlg = new CertificateStoreDialog(m_config, m_host.Main.Telemetry);
            await dlg.ShowDialog(owner).ConfigureAwait(true);
            // After the user closes the dialog, refresh in case they
            // added or removed certificates.
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"● Open trust dialog failed: {ex.Message}";
            m_log.LogWarning(ex, "Certificate Manager tab {Title} OpenTrustDialog failed.", Title);
        }
    }

    // ----- Per-certificate context-menu actions -----

    /// <summary>
    /// Render a self-contained details view for the selected certificate
    /// in a modal popup.  The detail rendering is built inline here
    /// rather than extracted to <c>Views/</c> to stay within this
    /// plug-in's owned file scope.
    /// </summary>
    public async Task ViewDetailsAsync()
    {
        Window? owner = GetOwnerWindow();
        if (owner is null || SelectedCertificate is null)
        {
            return;
        }
        try
        {
            var popup = BuildDetailsWindow(SelectedCertificate.Certificate);
            await popup.ShowDialog(owner).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"● View details failed: {ex.Message}";
            m_log.LogWarning(ex, "Certificate Manager tab {Title} ViewDetails failed.", Title);
        }
    }

    /// <summary>Move the selected certificate to the TrustedPeer store.</summary>
    public Task TrustToPeerAsync() => MoveSelectedAsync(CertStoreRole.TrustedPeer);

    /// <summary>Move the selected certificate to the TrustedIssuer store.</summary>
    public Task TrustToIssuerAsync() => MoveSelectedAsync(CertStoreRole.TrustedIssuer);

    /// <summary>Move the selected certificate to the Rejected store.</summary>
    public Task RejectAsync() => MoveSelectedAsync(CertStoreRole.Rejected);

    private async Task MoveSelectedAsync(CertStoreRole target)
    {
        if (SelectedStore is not { } src || SelectedCertificate is not { } row)
        {
            return;
        }
        CertStoreNode? dst = FindStore(target);
        if (dst is null)
        {
            Status = $"● {target} store not configured.";
            return;
        }
        if (dst.Identifier == src.Identifier
            || string.Equals(dst.Identifier.StorePath, src.Identifier.StorePath, StringComparison.OrdinalIgnoreCase))
        {
            Status = $"● Already in {dst.DisplayName}.";
            return;
        }

        try
        {
            bool added = await AddToStoreAsync(dst, row.Certificate).ConfigureAwait(true);
            if (!added)
            {
                Status = $"● Add to {dst.DisplayName} failed.";
                return;
            }
            bool removed = await DeleteFromStoreAsync(src, row.Thumbprint).ConfigureAwait(true);
            Status = removed
                ? $"● Moved {row.Subject} → {dst.DisplayName}."
                : $"● Copied {row.Subject} → {dst.DisplayName} (source remove failed).";
            await ReloadSelectedStoreAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"● Move to {dst.DisplayName} failed: {ex.Message}";
            m_log.LogWarning(ex, "Certificate Manager tab {Title} Move({Target}) failed.", Title, target);
        }
    }

    /// <summary>Delete the selected certificate from the currently-selected store.</summary>
    public async Task DeleteAsync()
    {
        if (SelectedStore is not { } src || SelectedCertificate is not { } row)
        {
            return;
        }
        try
        {
            bool ok = await DeleteFromStoreAsync(src, row.Thumbprint).ConfigureAwait(true);
            Status = ok
                ? $"● Deleted {row.Subject} from {src.DisplayName}."
                : $"● Delete failed for {row.Thumbprint}.";
            await ReloadSelectedStoreAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"● Delete failed: {ex.Message}";
            m_log.LogWarning(ex, "Certificate Manager tab {Title} Delete failed.", Title);
        }
    }

    /// <summary>
    /// Export the selected certificate to disk.  The user chooses the
    /// file type via the save-picker extension: <c>.pem</c> writes a
    /// PEM-encoded certificate, anything else writes raw DER bytes.
    /// </summary>
    public async Task ExportAsync()
    {
        Window? owner = GetOwnerWindow();
        if (owner is null || SelectedCertificate is null)
        {
            return;
        }
        try
        {
            string suggestedName = string.Format(CultureInfo.InvariantCulture,
                "{0}.cer", SelectedCertificate.Subject.Replace(' ', '_'));
            IStorageFile? file = await owner.StorageProvider
                .SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export certificate",
                    SuggestedFileName = suggestedName,
                    DefaultExtension = "cer",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("DER (X.509)") { Patterns = s_derExportPatterns },
                        new FilePickerFileType("PEM (X.509)") { Patterns = s_pemExportPatterns }
                    }
                }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }
            string path = file.Path.LocalPath;
            byte[] bytes;
            if (path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
            {
                using Certificate wrapper = Certificate.From(SelectedCertificate.Certificate);
                bytes = PEMWriter.ExportCertificateAsPEM(wrapper);
            }
            else
            {
                bytes = SelectedCertificate.Certificate.Export(X509ContentType.Cert);
            }
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(true);
            Status = string.Format(CultureInfo.InvariantCulture,
                "● Exported {0} ({1} bytes) → {2}",
                SelectedCertificate.Subject, bytes.Length, file.Name);
        }
        catch (Exception ex)
        {
            Status = $"● Export failed: {ex.Message}";
            m_log.LogWarning(ex, "Certificate Manager tab {Title} Export failed.", Title);
        }
    }

    /// <summary>
    /// Import a certificate from disk into the currently-selected
    /// store.  Accepts DER (<c>.cer</c> / <c>.crt</c> / <c>.der</c>)
    /// or PEM (<c>.pem</c>) — both are handled by
    /// <see cref="X509CertificateLoader.LoadCertificate(byte[])"/>.
    /// </summary>
    public async Task ImportAsync()
    {
        Window? owner = GetOwnerWindow();
        if (owner is null)
        {
            return;
        }
        if (SelectedStore is not { } dst)
        {
            Status = "● Pick a destination store first.";
            return;
        }
        try
        {
            IReadOnlyList<IStorageFile> files = await owner.StorageProvider
                .OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Import certificate into {dst.DisplayName}",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("X.509 certificate") { Patterns = s_certImportPatterns }
                    }
                }).ConfigureAwait(true);
            if (files.Count == 0)
            {
                return;
            }
            string path = files[0].Path.LocalPath;
            byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
            X509Certificate2 cert = X509CertificateLoader.LoadCertificate(bytes);
            bool ok = await AddToStoreAsync(dst, cert).ConfigureAwait(true);
            Status = ok
                ? $"● Imported {cert.Subject} → {dst.DisplayName}."
                : $"● Import failed for {Path.GetFileName(path)}.";
            await ReloadSelectedStoreAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"● Import failed: {ex.Message}";
            m_log.LogWarning(ex, "Certificate Manager tab {Title} Import failed.", Title);
        }
    }

    // ----- Helpers -----

    private static Window? GetOwnerWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desk)
        {
            return desk.MainWindow;
        }
        return null;
    }

    private static Window BuildDetailsWindow(X509Certificate2 cert)
    {
        var bg = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
        var fg = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
        var dim = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

        DateTime now = DateTime.UtcNow;
        string appUri;
        try
        {
            using Certificate wrapper = Certificate.From(cert);
            appUri = X509Utils.GetApplicationUriFromCertificate(wrapper) ?? "(none)";
        }
        catch (Exception)
        {
            appUri = "(unknown)";
        }
        string validity = string.Format(CultureInfo.InvariantCulture,
            "{0:u} → {1:u}",
            cert.NotBefore.ToUniversalTime(),
            cert.NotAfter.ToUniversalTime());
        if (cert.NotAfter.ToUniversalTime() < now)
        {
            validity += "   ⚠ EXPIRED";
        }
        else if (cert.NotBefore.ToUniversalTime() > now)
        {
            validity += "   ⚠ NOT YET VALID";
        }

        var grid = new Grid
        {
            Margin = new Avalonia.Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("160,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,*,Auto")
        };

        AddRow(grid, 0, "Subject", cert.Subject, fg, dim);
        AddRow(grid, 1, "Issuer", cert.Issuer, fg, dim);
        AddRow(grid, 2, "ApplicationUri", appUri, fg, dim);
        AddRow(grid, 3, "Valid", validity, fg, dim);
        AddRow(grid, 4, "Serial", cert.SerialNumber, fg, dim);
        AddRow(grid, 5, "Thumbprint", cert.Thumbprint, fg, dim);
        AddRow(grid, 6, "SignatureAlgorithm", cert.SignatureAlgorithm.FriendlyName ?? "(unknown)", fg, dim);
        AddRow(grid, 7, "HasPrivateKey", cert.HasPrivateKey ? "yes" : "no", fg, dim);

        var pem = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 11,
            Foreground = fg,
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20)),
            Margin = new Avalonia.Thickness(0, 12, 0, 0),
            MinHeight = 160
        };
        try
        {
            using Certificate wrapper = Certificate.From(cert);
            pem.Text = System.Text.Encoding.ASCII.GetString(PEMWriter.ExportCertificateAsPEM(wrapper));
        }
        catch (Exception)
        {
            pem.Text = "(unable to render PEM)";
        }
        Grid.SetRow(pem, 8);
        Grid.SetColumnSpan(pem, 2);
        grid.Children.Add(pem);

        var close = new Button
        {
            Content = "Close",
            IsCancel = true,
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(close, 9);
        Grid.SetColumnSpan(close, 2);
        grid.Children.Add(close);

        var window = new Window
        {
            Title = "Certificate details",
            Width = 720,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = bg,
            Foreground = fg,
            Content = grid
        };
        close.Click += (_, _) => window.Close();
        return window;
    }

    private static void AddRow(Grid grid, int row, string label, string value, IBrush fg, IBrush dim)
    {
        var k = new TextBlock
        {
            Text = label,
            Foreground = dim,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 12,
            Margin = new Avalonia.Thickness(0, 4, 8, 0)
        };
        var v = new TextBlock
        {
            Text = value,
            Foreground = fg,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        };
        Grid.SetRow(k, row);
        Grid.SetColumn(k, 0);
        Grid.SetRow(v, row);
        Grid.SetColumn(v, 1);
        grid.Children.Add(k);
        grid.Children.Add(v);
    }
}
