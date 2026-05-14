/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Views;

namespace UaLens.Plugins.GdsPush;

/// <summary>
/// Result returned by <see cref="CertificateRequestDialog"/>.  When
/// <see cref="Applied"/> is <c>true</c>, the user proceeded all the way
/// through step 4 and the dialog called <c>UpdateCertificate</c> on the
/// server.  When <c>false</c>, only a CSR was generated and the user
/// closed the wizard before applying the signed certificate.
/// </summary>
internal sealed record CertificateRequestResult(
    string GroupName,
    byte[] Csr,
    bool Applied);

/// <summary>
/// Four-step wizard for the GDS Push CSR + UpdateCertificate flow:
///   1. choose certificate group + type from the connected server
///   2. enter subject name + decide whether to regenerate the private key
///   3. server generates the CSR; user copies the PEM, signs externally
///   4. user pastes signed cert (+ optional issuer chain); UpdateCertificate
///      is invoked
/// All server-side work is delegated to <see cref="GdsCertRequestHelper"/>
/// via an injected <see cref="IGdsClientLike"/>, so the same wizard is
/// usable from either GDS Push or future GDS Management contexts.
/// </summary>
internal sealed partial class CertificateRequestDialog : Window
{
    private readonly IGdsClientLike m_client;
    private readonly IReadOnlyList<CertificateGroupChoice> m_groups;
    private int m_step;
    private byte[] m_csr = Array.Empty<byte>();
    private CertificateGroupChoice? m_selectedGroup;
    private bool m_applied;

    public CertificateRequestDialog()
    {
        m_client = null!;
        m_groups = Array.Empty<CertificateGroupChoice>();
        InitializeComponent();
    }

    public CertificateRequestDialog(
        IGdsClientLike client,
        IReadOnlyList<CertificateGroupChoice> groups)
    {
        m_client = client ?? throw new ArgumentNullException(nameof(client));
        m_groups = groups ?? throw new ArgumentNullException(nameof(groups));
        InitializeComponent();
        WireUp();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireUp()
    {
        var groupBox = this.RequiredControl<ComboBox>("GroupBox");
        var typeLabel = this.RequiredControl<TextBlock>("TypeLabel");
        var subjectBox = this.RequiredControl<TextBox>("SubjectBox");
        var back = this.RequiredControl<Button>("BackButton");
        var next = this.RequiredControl<Button>("NextButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        groupBox.ItemsSource = m_groups;
        if (m_groups.Count > 0)
        {
            groupBox.SelectedIndex = 0;
            m_selectedGroup = m_groups[0];
            typeLabel.Text = m_selectedGroup.CertificateTypeId.ToString();
            subjectBox.Text = "CN=Server";
        }
        groupBox.SelectionChanged += (_, _) =>
        {
            m_selectedGroup = groupBox.SelectedItem as CertificateGroupChoice;
            typeLabel.Text = m_selectedGroup?.CertificateTypeId.ToString() ?? string.Empty;
        };

        back.Click += async (_, _) => await OnBackAsync().ConfigureAwait(true);
        next.Click += async (_, _) => await OnNextAsync().ConfigureAwait(true);
        cancel.Click += (_, _) => Close(BuildResult());

        m_step = 1;
        RefreshUi();
    }

    private async Task OnBackAsync()
    {
        if (m_step > 1)
        {
            m_step--;
            RefreshUi();
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task OnNextAsync()
    {
        var err = this.RequiredControl<TextBlock>("ErrorLabel");
        SetError(err, null);
        var subjectBox = this.RequiredControl<TextBox>("SubjectBox");
        var regenBox = this.RequiredControl<CheckBox>("RegenKeyBox");
        var csrBox = this.RequiredControl<TextBox>("CsrBox");
        var signedBox = this.RequiredControl<TextBox>("SignedBox");
        var callApplyChangesBox = this.RequiredControl<CheckBox>("CallApplyChangesBox");
        var next = this.RequiredControl<Button>("NextButton");
        var back = this.RequiredControl<Button>("BackButton");

        try
        {
            switch (m_step)
            {
                case 1:
                    if (m_selectedGroup is null)
                    {
                        SetError(err, "Pick a certificate group first.");
                        return;
                    }
                    m_step = 2;
                    RefreshUi();
                    return;
                case 2:
                    string subject = subjectBox.Text ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(subject))
                    {
                        SetError(err, "Subject name is required.");
                        return;
                    }
                    if (m_selectedGroup is not { } group)
                    {
                        SetError(err, "Pick a certificate group first.");
                        return;
                    }
                    bool regen = regenBox.IsChecked == true;
                    next.IsEnabled = false;
                    back.IsEnabled = false;
                    try
                    {
                        m_csr = await GdsCertRequestHelper.GenerateCsrAsync(
                            m_client,
                            group.CertificateGroupId,
                            group.CertificateTypeId,
                            subject,
                            regen,
                            Array.Empty<byte>(),
                            CancellationToken.None).ConfigureAwait(true);
                    }
                    finally
                    {
                        next.IsEnabled = true;
                        back.IsEnabled = true;
                    }
                    csrBox.Text = GdsCertRequestHelper.ToPem(m_csr, "CERTIFICATE REQUEST");
                    m_step = 3;
                    RefreshUi();
                    return;
                case 3:
                    m_step = 4;
                    RefreshUi();
                    return;
                case 4:
                    string pem = signedBox.Text ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(pem))
                    {
                        SetError(err, "Paste the signed certificate PEM.");
                        return;
                    }
                    if (m_selectedGroup is not { } group2)
                    {
                        SetError(err, "Pick a certificate group first.");
                        return;
                    }
                    (byte[] cert, IReadOnlyList<byte[]> issuers) = ParseSignedBundle(pem);
                    next.IsEnabled = false;
                    back.IsEnabled = false;
                    try
                    {
                        await GdsCertRequestHelper.ApplyUpdatedCertificateAsync(
                            m_client,
                            group2.CertificateGroupId,
                            group2.CertificateTypeId,
                            cert,
                            issuers,
                            string.Empty,
                            Array.Empty<byte>(),
                            CancellationToken.None).ConfigureAwait(true);
                        m_applied = true;
                    }
                    finally
                    {
                        next.IsEnabled = true;
                        back.IsEnabled = true;
                    }
                    if (callApplyChangesBox.IsChecked == true)
                    {
                        // The Push adapter does not surface ApplyChanges,
                        // but the caller (GdsPushPlugin) does — surface
                        // the user's wish via a marker in the result.  Here
                        // we simply close; the parent has a separate Apply
                        // button.  Keep the dialog dismiss path simple.
                    }
                    Close(BuildResult());
                    return;
            }
        }
        catch (Exception ex)
        {
            SetError(err, $"Step {m_step} failed: {ex.Message}");
        }
    }

    private void RefreshUi()
    {
        var header = this.RequiredControl<TextBlock>("StepHeader");
        var step1 = this.RequiredControl<Control>("Step1");
        var step2 = this.RequiredControl<Control>("Step2");
        var step3 = this.RequiredControl<Control>("Step3");
        var step4 = this.RequiredControl<Control>("Step4");
        var back = this.RequiredControl<Button>("BackButton");
        var next = this.RequiredControl<Button>("NextButton");

        step1.IsVisible = m_step == 1;
        step2.IsVisible = m_step == 2;
        step3.IsVisible = m_step == 3;
        step4.IsVisible = m_step == 4;

        back.IsEnabled = m_step > 1;
        next.Content = m_step switch
        {
            1 => "Next →",
            2 => "Generate CSR",
            3 => "Next →",
            4 => "Update Certificate",
            _ => "Next →"
        };
        header.Text = m_step switch
        {
            1 => "Step 1 of 4: choose certificate group",
            2 => "Step 2 of 4: subject + key options",
            3 => "Step 3 of 4: CSR generated",
            4 => "Step 4 of 4: paste signed certificate",
            _ => "Certificate request"
        };
    }

    /// <summary>
    /// Splits a multi-PEM blob into the first CERTIFICATE block (the new
    /// app cert) and zero or more trailing CERTIFICATE blocks (the issuer
    /// chain).  Empty / malformed blocks are silently skipped.
    /// </summary>
    private static (byte[] Certificate, IReadOnlyList<byte[]> Issuers) ParseSignedBundle(string pem)
    {
        var blocks = new List<byte[]>();
        const string begin = "-----BEGIN CERTIFICATE-----";
        const string end = "-----END CERTIFICATE-----";
        int idx = 0;
        while (idx < pem.Length)
        {
            int b = pem.IndexOf(begin, idx, StringComparison.Ordinal);
            if (b < 0)
            {
                break;
            }

            int e = pem.IndexOf(end, b + begin.Length, StringComparison.Ordinal);
            if (e < 0)
            {
                break;
            }

            int blockEnd = e + end.Length;
            string block = pem.Substring(b, blockEnd - b);
            byte[] der = GdsCertRequestHelper.FromPem(block, "CERTIFICATE");
            blocks.Add(der);
            idx = blockEnd;
        }
        if (blocks.Count == 0)
        {
            throw new FormatException("No CERTIFICATE blocks found in pasted text.");
        }
        byte[] head = blocks[0];
        var tail = new List<byte[]>(blocks.Count - 1);
        for (int i = 1; i < blocks.Count; i++)
        {
            tail.Add(blocks[i]);
        }
        return (head, tail);
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

    private CertificateRequestResult? BuildResult()
    {
        if (m_selectedGroup is null || m_csr.Length == 0)
        {
            return null;
        }
        return new CertificateRequestResult(
            m_selectedGroup.Name,
            m_csr,
            m_applied);
    }
}
