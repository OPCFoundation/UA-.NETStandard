// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Views
{
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
            using (var certWrapper = new Opc.Ua.Security.Certificates.Certificate(cert.RawData))
            {
                var applicationUris = X509Utils.GetApplicationUrisFromCertificate(certWrapper);
                this.RequiredControl<TextBlock>("AppUriLabel").Text =
                    applicationUris.Count == 0 ? "(none)" : applicationUris[0];
            }
            DateTime nowUtc = DateTime.UtcNow;
            TextBlock notBefore = this.RequiredControl<TextBlock>("NotBeforeLabel");
            TextBlock notAfter = this.RequiredControl<TextBlock>("NotAfterLabel");
            notBefore.Text = cert.NotBefore.ToString("u", CultureInfo.InvariantCulture);
            notAfter.Text = cert.NotAfter.ToString("u", CultureInfo.InvariantCulture);
            IBrush warnBrush = (Application.Current?.FindResource("ErrorText") as IBrush)
                ?? Brushes.Transparent;
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
}
