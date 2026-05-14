/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Views;

internal sealed partial class CertificateTrustDialog : Window
{
    public TrustChoice Choice { get; private set; } = TrustChoice.Reject;

    public CertificateTrustDialog(X509Certificate2 cert, ServiceResult error)
    {
        InitializeComponent();

        this.RequiredControl<TextBlock>("ErrorLabel").Text =
            $"The server certificate failed validation: {error.StatusCode} — {error.LocalizedText}";
        this.RequiredControl<TextBlock>("SubjectLabel").Text = cert.Subject;
        this.RequiredControl<TextBlock>("IssuerLabel").Text = cert.Issuer;
        using (var certWrapper = Opc.Ua.Security.Certificates.Certificate.From(cert))
        {
            this.RequiredControl<TextBlock>("AppUriLabel").Text = X509Utils.GetApplicationUriFromCertificate(certWrapper) ?? "(none)";
        }
        DateTime nowUtc = DateTime.UtcNow;
        var notBefore = this.RequiredControl<TextBlock>("NotBeforeLabel");
        var notAfter = this.RequiredControl<TextBlock>("NotAfterLabel");
        notBefore.Text = cert.NotBefore.ToString("u", CultureInfo.InvariantCulture);
        notAfter.Text = cert.NotAfter.ToString("u", CultureInfo.InvariantCulture);
        var warnBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF8, 0x71, 0x71));
        if (cert.NotBefore.ToUniversalTime() > nowUtc)
        {
            notBefore.Text += "  ⚠ NOT YET VALID";
            notBefore.Foreground = warnBrush;
        }
        if (cert.NotAfter.ToUniversalTime() < nowUtc)
        {
            notAfter.Text += "  ⚠ EXPIRED";
            notAfter.Foreground = warnBrush;
        }
        this.RequiredControl<TextBlock>("ThumbprintLabel").Text = cert.Thumbprint;
        this.RequiredControl<TextBlock>("StatusCodeLabel").Text = error.ToLongString();

        this.RequiredControl<Button>("AcceptOnceButton").Click += (_, _) =>
        {
            Choice = TrustChoice.AcceptOnce;
            Close(Choice);
        };
        this.RequiredControl<Button>("TrustButton").Click += (_, _) =>
        {
            Choice = TrustChoice.TrustPermanently;
            Close(Choice);
        };
        this.RequiredControl<Button>("RejectButton").Click += (_, _) =>
        {
            Choice = TrustChoice.Reject;
            Close(Choice);
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
