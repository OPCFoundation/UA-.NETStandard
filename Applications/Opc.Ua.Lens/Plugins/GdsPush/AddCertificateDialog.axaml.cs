/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using UaLens.Views;

namespace UaLens.Plugins.GdsPush;

/// <summary>
/// Result returned by <see cref="AddCertificateDialog"/> on OK.  The
/// dialog has already verified that <see cref="Certificate"/> is parseable
/// and the user has chosen <see cref="Bucket"/>.
/// </summary>
internal sealed record AddCertificateResult(X509Certificate2 Certificate, TrustListBucket Bucket);

/// <summary>
/// Modal dialog for adding a certificate to a server's GDS Push trust
/// list.  Accepts either a file (DER or PEM) or a PEM block pasted into
/// the text box.  Selection radio buttons pick the destination (Trusted
/// Peers or Issuers); the initial selection is taken from the bucket that
/// was active in the parent view.
/// </summary>
internal sealed partial class AddCertificateDialog : Window
{
    private static readonly string[] s_certPatterns = ["*.der", "*.crt", "*.cer", "*.pem"];

    private string? m_filePath;

    public AddCertificateDialog() : this(TrustListBucket.Trusted)
    {
    }

    public AddCertificateDialog(TrustListBucket initialBucket)
    {
        InitializeComponent();
        WireUp(initialBucket);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireUp(TrustListBucket initialBucket)
    {
        var destTrusted = this.RequiredControl<RadioButton>("DestTrusted");
        var destIssuer = this.RequiredControl<RadioButton>("DestIssuer");
        var browse = this.RequiredControl<Button>("BrowseButton");
        var fileLabel = this.RequiredControl<TextBlock>("FileLabel");
        var pemBox = this.RequiredControl<TextBox>("PemBox");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        var err = this.RequiredControl<TextBlock>("ErrorLabel");

        // Rejected is not a valid destination for Add (server-managed) —
        // fall back to Trusted in that case.
        if (initialBucket == TrustListBucket.Issuer)
        {
            destIssuer.IsChecked = true;
            destTrusted.IsChecked = false;
        }
        else
        {
            destTrusted.IsChecked = true;
            destIssuer.IsChecked = false;
        }

        browse.Click += async (_, _) =>
        {
            try
            {
                IStorageProvider sp = StorageProvider;
                IReadOnlyList<IStorageFile> files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Add certificate",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("X.509 certificate")
                        {
                            Patterns = s_certPatterns
                        }
                    }
                }).ConfigureAwait(true);
                if (files.Count == 0)
                {
                    return;
                }

                m_filePath = files[0].Path.LocalPath;
                fileLabel.Text = m_filePath;
                SetError(err, null);
            }
            catch (Exception ex)
            {
                SetError(err, $"File picker failed: {ex.Message}");
            }
        };

        ok.Click += async (_, _) =>
        {
            try
            {
                X509Certificate2 cert;
                if (!string.IsNullOrEmpty(m_filePath))
                {
                    cert = await GdsCertRequestHelper
                        .LoadCertificateFromFileAsync(m_filePath, CancellationToken.None)
                        .ConfigureAwait(true);
                }
                else
                {
                    string pem = pemBox.Text ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(pem))
                    {
                        SetError(err, "Pick a file or paste a PEM block.");
                        return;
                    }
                    byte[] der = GdsCertRequestHelper.FromPem(pem, "CERTIFICATE");
                    cert = X509CertificateLoader.LoadCertificate(der);
                }
                TrustListBucket bucket = destIssuer.IsChecked == true
                    ? TrustListBucket.Issuer
                    : TrustListBucket.Trusted;
                Close(new AddCertificateResult(cert, bucket));
            }
            catch (Exception ex)
            {
                SetError(err, $"Parse failed: {ex.Message}");
            }
        };

        cancel.Click += (_, _) => Close(null);
    }

    private static void SetError(TextBlock err, string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            err.IsVisible = false;
            err.Text = string.Empty;
        }
        else
        {
            err.IsVisible = true;
            err.Text = message;
        }
    }
}
