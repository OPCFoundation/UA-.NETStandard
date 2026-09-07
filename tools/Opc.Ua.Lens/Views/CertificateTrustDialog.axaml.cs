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
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
        var warnBrush = (Application.Current?.FindResource("AccentRedLight") as IBrush)
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
