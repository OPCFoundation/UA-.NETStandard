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
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Views;

/// <summary>
/// Certificate-store-management dialog.  Three tabs (Trusted Peers /
/// Trusted Issuers / Rejected) each enumerating the corresponding
/// <see cref="ICertificateStore"/> via <see cref="CertificateStoreService"/>.
/// Per-tab actions: Refresh, Untrust/Trust selection, Delete-expired bulk.
/// </summary>
internal sealed partial class CertificateStoreDialog : Window
{
    private static readonly string[] s_certPatterns = ["*.cer", "*.crt", "*.der", "*.pem"];

    private readonly CertificateStoreService m_service;
    public ObservableCollection<CertRow> Trusted { get; } = new();
    public ObservableCollection<CertRow> Issuer { get; } = new();
    public ObservableCollection<CertRow> Rejected { get; } = new();

    public CertificateStoreDialog()
    {
        m_service = null!;
        InitializeComponent();
    }

    public CertificateStoreDialog(ApplicationConfiguration config, ITelemetryContext telemetry)
    {
        m_service = new CertificateStoreService(config, telemetry);
        InitializeComponent();
        WireUp();
        _ = ReloadAllAsync();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireUp()
    {
        var trustedList = this.RequiredControl<ListBox>("TrustedList");
        var issuerList = this.RequiredControl<ListBox>("IssuerList");
        var rejectedList = this.RequiredControl<ListBox>("RejectedList");
        trustedList.ItemsSource = Trusted;
        issuerList.ItemsSource = Issuer;
        rejectedList.ItemsSource = Rejected;

        this.RequiredControl<Button>("TrustedRefreshBtn").Click += async (_, _) => await ReloadAsync(CertStoreKind.Trusted).ConfigureAwait(false);
        this.RequiredControl<Button>("IssuerRefreshBtn").Click += async (_, _) => await ReloadAsync(CertStoreKind.Issuer).ConfigureAwait(false);
        this.RequiredControl<Button>("RejectedRefreshBtn").Click += async (_, _) => await ReloadAsync(CertStoreKind.Rejected).ConfigureAwait(false);

        this.RequiredControl<Button>("TrustedUntrustBtn").Click += async (_, _) =>
            await UntrustSelectedAsync(CertStoreKind.Trusted, trustedList).ConfigureAwait(false);
        this.RequiredControl<Button>("IssuerUntrustBtn").Click += async (_, _) =>
            await UntrustSelectedAsync(CertStoreKind.Issuer, issuerList).ConfigureAwait(false);

        this.RequiredControl<Button>("TrustedExpireBtn").Click += async (_, _) =>
            await DeleteExpiredAsync(CertStoreKind.Trusted).ConfigureAwait(false);
        this.RequiredControl<Button>("IssuerExpireBtn").Click += async (_, _) =>
            await DeleteExpiredAsync(CertStoreKind.Issuer).ConfigureAwait(false);

        this.RequiredControl<Button>("TrustedAddBtn").Click += async (_, _) =>
            await AddFromFileAsync(CertStoreKind.Trusted).ConfigureAwait(false);
        this.RequiredControl<Button>("IssuerAddBtn").Click += async (_, _) =>
            await AddFromFileAsync(CertStoreKind.Issuer).ConfigureAwait(false);

        this.RequiredControl<Button>("RejectedTrustBtn").Click += async (_, _) =>
        {
            if (rejectedList.SelectedItem is not CertRow row)
            {
                return;
            }

            try
            {
                bool ok = await m_service.TrustRejectedAsync(row.Thumbprint).ConfigureAwait(true);
                SetStatus(ok ? $"Moved {row.Subject} to Trusted Peers." : "Trust failed.");
            }
            catch (Exception ex) { SetStatus($"Trust failed: {ex.Message}"); }
            await ReloadAsync(CertStoreKind.Rejected).ConfigureAwait(false);
            await ReloadAsync(CertStoreKind.Trusted).ConfigureAwait(false);
        };
        this.RequiredControl<Button>("RejectedDeleteBtn").Click += async (_, _) =>
            await UntrustSelectedAsync(CertStoreKind.Rejected, rejectedList).ConfigureAwait(false);
        this.RequiredControl<Button>("RejectedClearBtn").Click += async (_, _) =>
        {
            try
            {
                IReadOnlyList<X509Certificate2> all = await m_service.ListAsync(CertStoreKind.Rejected).ConfigureAwait(true);
                int n = 0;
                foreach (X509Certificate2 c in all)
                {
                    if (await m_service.DeleteAsync(CertStoreKind.Rejected, c.Thumbprint).ConfigureAwait(true))
                    {
                        n++;
                    }
                }
                SetStatus($"Cleared {n} rejected certificate(s).");
            }
            catch (Exception ex) { SetStatus($"Clear failed: {ex.Message}"); }
            await ReloadAsync(CertStoreKind.Rejected).ConfigureAwait(false);
        };
    }

    private async Task ReloadAllAsync()
    {
        await Task.WhenAll(
            ReloadAsync(CertStoreKind.Trusted),
            ReloadAsync(CertStoreKind.Issuer),
            ReloadAsync(CertStoreKind.Rejected)).ConfigureAwait(false);
    }

    private async Task ReloadAsync(CertStoreKind kind)
    {
        try
        {
            IReadOnlyList<X509Certificate2> certs = await m_service.ListAsync(kind).ConfigureAwait(true);
            ObservableCollection<CertRow> target = TargetFor(kind);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                target.Clear();
                foreach (X509Certificate2 c in certs)
                {
                    target.Add(CertRow.From(c));
                }
            });
            SetStatus($"{kind}: {certs.Count} certificate(s).");
        }
        catch (Exception ex)
        {
            SetStatus($"Load {kind} failed: {ex.Message}");
        }
    }

    private async Task UntrustSelectedAsync(CertStoreKind kind, ListBox list)
    {
        if (list.SelectedItem is not CertRow row)
        {
            return;
        }

        try
        {
            bool ok = await m_service.DeleteAsync(kind, row.Thumbprint).ConfigureAwait(true);
            SetStatus(ok ? $"Removed {row.Subject} from {kind}." : "Delete failed.");
        }
        catch (Exception ex) { SetStatus($"Delete failed: {ex.Message}"); }
        await ReloadAsync(kind).ConfigureAwait(false);
    }

    private async Task DeleteExpiredAsync(CertStoreKind kind)
    {
        try
        {
            int n = await m_service.DeleteExpiredAsync(kind).ConfigureAwait(true);
            SetStatus($"Deleted {n} expired certificate(s) from {kind}.");
        }
        catch (Exception ex) { SetStatus($"Delete-expired failed: {ex.Message}"); }
        await ReloadAsync(kind).ConfigureAwait(false);
    }

    /// <summary>
    /// Add from file flow: file picker → load via X509CertificateLoader →
    /// service.AddAsync.  Supports DER (.cer / .crt / .der) and PEM (.pem).
    /// </summary>
    private async Task AddFromFileAsync(CertStoreKind kind)
    {
        try
        {
            Avalonia.Platform.Storage.IStorageProvider sp = StorageProvider;
            System.Collections.Generic.IReadOnlyList<Avalonia.Platform.Storage.IStorageFile> files =
                await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = $"Add certificate to {kind}",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("X.509 certificate")
                        {
                            Patterns = s_certPatterns
                        }
                    }
                }).ConfigureAwait(true);
            if (files.Count == 0)
            {
                return;
            }

            string path = files[0].Path.LocalPath;
            byte[] bytes = await System.IO.File.ReadAllBytesAsync(path).ConfigureAwait(true);
            X509Certificate2 cert = X509CertificateLoader.LoadCertificate(bytes);
            bool ok = await m_service.AddAsync(kind, cert).ConfigureAwait(true);
            SetStatus(ok
                ? $"Added {cert.Subject} to {kind}."
                : $"Add failed for {System.IO.Path.GetFileName(path)}.");
        }
        catch (Exception ex) { SetStatus($"Add failed: {ex.Message}"); }
        await ReloadAsync(kind).ConfigureAwait(false);
    }

    private ObservableCollection<CertRow> TargetFor(CertStoreKind kind) => kind switch
    {
        CertStoreKind.Trusted => Trusted,
        CertStoreKind.Issuer => Issuer,
        CertStoreKind.Rejected => Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private void SetStatus(string text)
    {
        var lbl = this.FindControl<TextBlock>("StatusLabel");
        if (lbl is not null)
        {
            Dispatcher.UIThread.Post(() => lbl.Text = text);
        }
    }
}

/// <summary>One row in the certificate-store DataGrid.</summary>
internal sealed record CertRow(
    string Subject,
    string Issuer,
    string NotBefore,
    string NotAfter,
    string Thumbprint,
    string Status)
{
    public static CertRow From(X509Certificate2 cert)
    {
        DateTime now = DateTime.UtcNow;
        DateTime nb = cert.NotBefore.ToUniversalTime();
        DateTime na = cert.NotAfter.ToUniversalTime();
        string status =
            na < now ? "EXPIRED"
            : nb > now ? "NOT YET VALID"
            : "OK";
        return new CertRow(
            ShortName(cert.Subject),
            ShortName(cert.Issuer),
            nb.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            na.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            cert.Thumbprint,
            status);
    }

    private static string ShortName(string distinguished)
    {
        // X.509 DNs are comma-separated RDNs.  The CN= component is by far
        // the most useful for at-a-glance identification.
        foreach (string rdn in distinguished.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rdn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return rdn[3..];
            }
        }
        return distinguished;
    }
}
