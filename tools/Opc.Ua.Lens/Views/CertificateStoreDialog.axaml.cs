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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
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
    public CertificateStoreDialog()
    {
        InitializeComponent();
        this.RequiredControl<Button>("CloseBtn").Click += (_, _) => Close();
    }

    public CertificateStoreDialog(ApplicationConfiguration config, ITelemetryContext telemetry)
        : this(new CertificateStoreOperations(new CertificateStoreService(config, telemetry)))
    {
    }

    public CertificateStoreDialog(CertificateStoreOperations operations)
    {
        m_operations = operations ?? throw new ArgumentNullException(nameof(operations));
        InitializeComponent();
        WireUp();
        InitialLoad = ReloadAllAsync();
    }

    /// <summary>
    /// Completes after all three initial store listings finish, including reported listing failures.
    /// </summary>
    public Task InitialLoad { get; } = Task.CompletedTask;

    public ObservableCollection<CertRow> Trusted => m_operations!.Trusted;

    public ObservableCollection<CertRow> Issuer => m_operations!.Issuers;

    public ObservableCollection<CertRow> Rejected => m_operations!.Rejected;

    /// <summary>
    /// Shows the modal and restores the owner's prior focus when it is dismissed.
    /// </summary>
    public async Task ShowAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Avalonia.Input.IInputElement? previousFocus = owner.FocusManager?.GetFocusedElement();
        try
        {
            await ShowDialog(owner).ConfigureAwait(true);
        }
        finally
        {
            previousFocus?.Focus();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (m_operations is not null)
        {
            m_operations.PropertyChanged -= OnOperationChanged;
        }
        base.OnClosed(e);
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
        m_statusLabel = this.FindControl<TextBlock>("StatusLabel");
        m_resultLabel = this.FindControl<TextBlock>("ResultLabel");
        m_storeTabs = this.RequiredControl<TabControl>("StoreTabs");
        m_operations!.PropertyChanged += OnOperationChanged;
        this.RequiredControl<Button>("CloseBtn").Click += (_, _) => Close();
        this.RequiredControl<Button>("AcknowledgeBtn").Click += (_, _) => m_operations.AcknowledgeResult();
        trustedList.ItemsSource = Trusted;
        issuerList.ItemsSource = Issuer;
        rejectedList.ItemsSource = Rejected;

        this.RequiredControl<Button>("TrustedRefreshBtn").Click += async (_, _) =>
            await m_operations.ReloadAsync(CertStoreKind.Trusted).ConfigureAwait(true);
        this.RequiredControl<Button>("IssuerRefreshBtn").Click += async (_, _) =>
            await m_operations.ReloadAsync(CertStoreKind.Issuer).ConfigureAwait(true);
        this.RequiredControl<Button>("RejectedRefreshBtn").Click += async (_, _) =>
            await m_operations.ReloadAsync(CertStoreKind.Rejected).ConfigureAwait(true);

        this.RequiredControl<Button>("TrustedUntrustBtn").Click += async (_, _) =>
            await UntrustSelectedAsync(CertStoreKind.Trusted, trustedList).ConfigureAwait(true);
        this.RequiredControl<Button>("IssuerUntrustBtn").Click += async (_, _) =>
            await UntrustSelectedAsync(CertStoreKind.Issuer, issuerList).ConfigureAwait(true);

        this.RequiredControl<Button>("TrustedExpireBtn").Click += async (_, _) =>
            await m_operations.DeleteAllAsync(CertStoreKind.Trusted, expiredOnly: true).ConfigureAwait(true);
        this.RequiredControl<Button>("IssuerExpireBtn").Click += async (_, _) =>
            await m_operations.DeleteAllAsync(CertStoreKind.Issuer, expiredOnly: true).ConfigureAwait(true);

        this.RequiredControl<Button>("TrustedAddBtn").Click += async (_, _) =>
            await AddFromFileAsync(CertStoreKind.Trusted).ConfigureAwait(true);
        this.RequiredControl<Button>("IssuerAddBtn").Click += async (_, _) =>
            await AddFromFileAsync(CertStoreKind.Issuer).ConfigureAwait(true);

        this.RequiredControl<Button>("RejectedTrustBtn").Click += async (_, _) =>
        {
            if (rejectedList.SelectedItem is not CertRow row)
            {
                return;
            }

            await m_operations.TrustAsync(row.Thumbprint).ConfigureAwait(true);
        };
        this.RequiredControl<Button>("RejectedDeleteBtn").Click += async (_, _) =>
            await UntrustSelectedAsync(CertStoreKind.Rejected, rejectedList).ConfigureAwait(true);
        this.RequiredControl<Button>("RejectedClearBtn").Click += async (_, _) =>
            await m_operations.DeleteAllAsync(CertStoreKind.Rejected, expiredOnly: false).ConfigureAwait(true);
        UpdateOperationStatus();
    }

    private async Task ReloadAllAsync()
    {
        await m_operations!.ReloadAsync(CertStoreKind.Trusted).ConfigureAwait(true);
        await m_operations.ReloadAsync(CertStoreKind.Issuer).ConfigureAwait(true);
        await m_operations.ReloadAsync(CertStoreKind.Rejected).ConfigureAwait(true);
    }

    private async Task UntrustSelectedAsync(CertStoreKind kind, ListBox list)
    {
        if (list.SelectedItem is not CertRow row)
        {
            return;
        }

        await m_operations!.DeleteAsync(kind, row.Thumbprint).ConfigureAwait(true);
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
            using X509Certificate2 cert = X509CertificateLoader.LoadCertificate(bytes);
            await m_operations!.AddAsync(kind, cert).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            m_operations!.ReportFailure("Add", kind, ex.Message);
            await m_operations.ReloadAsync(kind).ConfigureAwait(true);
        }
    }

    private void OnOperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateOperationStatus();
        }
        else
        {
            Dispatcher.UIThread.Post(UpdateOperationStatus);
        }
    }

    private void UpdateOperationStatus()
    {
        if (m_statusLabel is not null)
        {
            m_statusLabel.Text = m_operations!.LoadingStatus;
        }
        if (m_resultLabel is not null)
        {
            m_resultLabel.Text = m_operations!.LastResult?.Summary ?? string.Empty;
        }
        if (m_storeTabs is not null)
        {
            m_storeTabs.IsEnabled = !m_operations!.IsBusy;
        }
    }

    private static readonly string[] s_certPatterns = ["*.cer", "*.crt", "*.der", "*.pem"];
    private readonly CertificateStoreOperations? m_operations;
    private TextBlock? m_statusLabel;
    private TextBlock? m_resultLabel;
    private TabControl? m_storeTabs;
}

/// <summary>
/// One row in the certificate-store DataGrid.
/// </summary>
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
        foreach (string rdn in distinguished.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rdn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return rdn[3..];
            }
        }
        return distinguished;
    }
}
