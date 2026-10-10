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
using System.IO;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using UaLens.Connection;
using UaLens.Plugins.Gds;
using UaLens.Views;

namespace UaLens.Plugins.GdsManagement;

/// <summary>
/// Modal dialog that collects the fields required to register an
/// application with <see cref="GlobalDiscoveryServerClient.RegisterApplicationAsync"/>.
/// The dialog returns a populated <see cref="RegisteredApplicationContext"/>
/// (or <c>null</c> on cancel). The dialog itself does not call the GDS —
/// the caller derives the <see cref="ApplicationRecordDataType"/> from
/// the returned context and dispatches the actual registration.
/// </summary>
/// <remarks>
/// The dialog supports the three registration flows from the
/// <c>UA-.NETStandard-Samples</c> GDS client:
/// <list type="bullet">
/// <item><see cref="GdsRegistrationType.ClientPull"/> / <see cref="GdsRegistrationType.ServerPull"/>
/// — exposes cert / trust-list local paths + HTTPS counterparts in an Expander.</item>
/// <item><see cref="GdsRegistrationType.ServerPush"/> — exposes a push-endpoint URL with a
/// <em>Pick…</em> button that defers to <see cref="EndpointCredentialsPicker.PromptAsync"/>.</item>
/// </list>
/// </remarks>
internal sealed partial class RegisterApplicationDialog : Window
{
    private static readonly string[] s_jsonPatterns = ["*.json"];
    private static readonly string[] s_loadPatterns = ["*.json", "*.xml"];

    private readonly ITelemetryContext? m_telemetry;
    private EndpointDescription? m_pickedPushEndpoint;

    /// <summary>Parameterless ctor for XAML / design preview only.</summary>
    public RegisterApplicationDialog()
        : this(telemetry: null, prefill: null)
    {
    }

    /// <param name="telemetry">Telemetry context used by the push endpoint picker.
    /// When <c>null</c> the <em>Pick…</em> button is disabled.</param>
    /// <param name="prefill">Optional context to pre-populate the form
    /// (useful when re-registering an existing app).</param>
    public RegisterApplicationDialog(ITelemetryContext? telemetry, RegisteredApplicationContext? prefill)
    {
        m_telemetry = telemetry;
        InitializeComponent();
        WireUp(prefill);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireUp(RegisteredApplicationContext? prefill)
    {
        var typeBox = this.RequiredControl<ComboBox>("RegistrationTypeBox");
        var nameBox = this.RequiredControl<TextBox>("NameBox");
        var uriBox = this.RequiredControl<TextBox>("UriBox");
        var productUriBox = this.RequiredControl<TextBox>("ProductUriBox");
        var discoveryBox = this.RequiredControl<TextBox>("DiscoveryBox");
        var capsBox = this.RequiredControl<TextBox>("CapabilitiesBox");

        var pullPanel = this.RequiredControl<StackPanel>("PullPanel");
        var pushPanel = this.RequiredControl<StackPanel>("PushPanel");

        var certStore = this.RequiredControl<TextBox>("CertStorePathBox");
        var subject = this.RequiredControl<TextBox>("SubjectNameBox");
        var publicKey = this.RequiredControl<TextBox>("PublicKeyPathBox");
        var privateKey = this.RequiredControl<TextBox>("PrivateKeyPathBox");
        var trustList = this.RequiredControl<TextBox>("TrustListStorePathBox");
        var issuerList = this.RequiredControl<TextBox>("IssuerListStorePathBox");
        var domains = this.RequiredControl<TextBox>("DomainsBox");

        var httpsPublic = this.RequiredControl<TextBox>("HttpsPublicKeyPathBox");
        var httpsPrivate = this.RequiredControl<TextBox>("HttpsPrivateKeyPathBox");
        var httpsTrust = this.RequiredControl<TextBox>("HttpsTrustListStorePathBox");
        var httpsIssuer = this.RequiredControl<TextBox>("HttpsIssuerListStorePathBox");

        var pushUrl = this.RequiredControl<TextBox>("PushEndpointUrlBox");
        var pickEndpoint = this.RequiredControl<Button>("PickEndpointButton");
        var pushSecurity = this.RequiredControl<TextBlock>("PushSecurityLabel");

        var loadButton = this.RequiredControl<Button>("LoadButton");
        var saveButton = this.RequiredControl<Button>("SaveButton");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        var err = this.RequiredControl<TextBlock>("ErrorLabel");

        typeBox.SelectedIndex = 0;
        typeBox.SelectionChanged += (_, _) => UpdateModePanels(typeBox, pullPanel, pushPanel);

        if (prefill is not null)
        {
            ApplyContext(
                prefill,
                typeBox, nameBox, uriBox, productUriBox,
                discoveryBox, capsBox,
                certStore, subject, publicKey, privateKey, trustList, issuerList, domains,
                httpsPublic, httpsPrivate, httpsTrust, httpsIssuer,
                pushUrl, pushSecurity);
        }

        UpdateModePanels(typeBox, pullPanel, pushPanel);
        pickEndpoint.IsEnabled = m_telemetry is not null;

        pickEndpoint.Click += async (_, _) =>
        {
            try
            {
                SetError(err, null);
                if (m_telemetry is null)
                {
                    SetError(err, "Endpoint picker unavailable (no telemetry context).");
                    return;
                }
                string seed = (pushUrl.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(seed))
                {
                    SetError(err, "Enter a discovery URL before picking an endpoint.");
                    return;
                }
                EndpointCredentialsPicker.Result? pick = await EndpointCredentialsPicker
                    .PromptAsync(this, m_telemetry, seed, CancellationToken.None)
                    .ConfigureAwait(true);
                if (pick is null)
                {
                    return;
                }
                m_pickedPushEndpoint = pick.Endpoint;
                pushUrl.Text = pick.Endpoint.EndpointUrl;
                pushSecurity.Text =
                    $"{pick.Endpoint.SecurityMode} / {pick.Endpoint.SecurityPolicyUri}";
            }
            catch (Exception ex)
            {
                SetError(err, $"Endpoint picker failed: {ex.Message}");
            }
        };

        loadButton.Click += async (_, _) =>
        {
            try
            {
                SetError(err, null);
                IStorageProvider sp = StorageProvider;
                IReadOnlyList<IStorageFile> files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Load application context",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("RegisteredApplicationContext (*.json, *.xml)")
                        {
                            Patterns = s_loadPatterns
                        }
                    }
                }).ConfigureAwait(true);
                if (files.Count == 0)
                {
                    return;
                }
                string path = files[0].Path.LocalPath;
                RegisteredApplicationContext loaded = RegisteredApplicationContextXml.Load(path);
                ApplyContext(
                    loaded,
                    typeBox, nameBox, uriBox, productUriBox,
                    discoveryBox, capsBox,
                    certStore, subject, publicKey, privateKey, trustList, issuerList, domains,
                    httpsPublic, httpsPrivate, httpsTrust, httpsIssuer,
                    pushUrl, pushSecurity);
                UpdateModePanels(typeBox, pullPanel, pushPanel);
            }
            catch (Exception ex)
            {
                SetError(err, $"Load failed: {ex.Message}");
            }
        };

        saveButton.Click += async (_, _) =>
        {
            try
            {
                SetError(err, null);
                RegisteredApplicationContext snapshot = BuildContext(
                    typeBox, nameBox, uriBox, productUriBox,
                    discoveryBox, capsBox,
                    certStore, subject, publicKey, privateKey, trustList, issuerList, domains,
                    httpsPublic, httpsPrivate, httpsTrust, httpsIssuer,
                    pushUrl,
                    requireMandatory: false);

                IStorageProvider sp = StorageProvider;
                IStorageFile? target = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save application context",
                    SuggestedFileName = SuggestFileName(snapshot),
                    DefaultExtension = "json",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("RegisteredApplicationContext (*.json)")
                        {
                            Patterns = s_jsonPatterns
                        }
                    }
                }).ConfigureAwait(true);
                if (target is null)
                {
                    return;
                }
                RegisteredApplicationContextXml.Save(snapshot, target.Path.LocalPath);
            }
            catch (Exception ex)
            {
                SetError(err, $"Save failed: {ex.Message}");
            }
        };

        ok.Click += (_, _) =>
        {
            try
            {
                SetError(err, null);
                RegisteredApplicationContext result = BuildContext(
                    typeBox, nameBox, uriBox, productUriBox,
                    discoveryBox, capsBox,
                    certStore, subject, publicKey, privateKey, trustList, issuerList, domains,
                    httpsPublic, httpsPrivate, httpsTrust, httpsIssuer,
                    pushUrl,
                    requireMandatory: true);
                Close(result);
            }
            catch (ArgumentException ax)
            {
                SetError(err, ax.Message);
            }
            catch (Exception ex)
            {
                SetError(err, $"Validation failed: {ex.Message}");
            }
        };
        cancel.Click += (_, _) => Close(null);
    }

    private RegisteredApplicationContext BuildContext(
        ComboBox typeBox,
        TextBox nameBox, TextBox uriBox, TextBox productUriBox,
        TextBox discoveryBox, TextBox capsBox,
        TextBox certStore, TextBox subject, TextBox publicKey, TextBox privateKey,
        TextBox trustList, TextBox issuerList, TextBox domains,
        TextBox httpsPublic, TextBox httpsPrivate, TextBox httpsTrust, TextBox httpsIssuer,
        TextBox pushUrl,
        bool requireMandatory)
    {
        GdsRegistrationType regType = ParseRegistrationType(typeBox.SelectedIndex);

        string name = (nameBox.Text ?? string.Empty).Trim();
        string uri = (uriBox.Text ?? string.Empty).Trim();
        string product = (productUriBox.Text ?? string.Empty).Trim();

        if (requireMandatory)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Application Name is required.");
            }
            if (string.IsNullOrEmpty(uri))
            {
                throw new ArgumentException("Application URI is required.");
            }
            if (string.IsNullOrEmpty(product))
            {
                throw new ArgumentException("Product URI is required.");
            }
            if (regType == GdsRegistrationType.ServerPush
                && string.IsNullOrWhiteSpace(pushUrl.Text))
            {
                throw new ArgumentException("Push endpoint URL is required for ServerPush mode.");
            }
        }

        List<string> discoveryList = SplitLines(discoveryBox.Text);
        List<string> capabilityList = SplitTokens(capsBox.Text);

        EndpointDescription? pushEndpoint = null;
        if (regType == GdsRegistrationType.ServerPush)
        {
            string url = (pushUrl.Text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(url))
            {
                if (m_pickedPushEndpoint is { } picked
                    && string.Equals(picked.EndpointUrl, url, StringComparison.OrdinalIgnoreCase))
                {
                    pushEndpoint = picked;
                }
                else
                {
                    // User typed a URL without invoking the picker — fall back to
                    // a sensible default (SignAndEncrypt, no concrete policy).
                    pushEndpoint = new EndpointDescription
                    {
                        EndpointUrl = url,
                        SecurityMode = MessageSecurityMode.SignAndEncrypt,
                        SecurityPolicyUri = string.Empty
                    };
                }
            }
        }

        return new RegisteredApplicationContext(
            ApplicationId: NodeId.Null,
            ApplicationUri: uri,
            ApplicationName: name,
            ProductUri: product,
            RegistrationType: regType,
            DiscoveryUrls: discoveryList.AsReadOnly(),
            ServerCapabilities: capabilityList.AsReadOnly(),
            Domains: NullIfEmpty(domains.Text),
            CertificateStorePath: NullIfEmpty(certStore.Text),
            CertificateSubjectName: NullIfEmpty(subject.Text),
            CertificatePublicKeyPath: NullIfEmpty(publicKey.Text),
            CertificatePrivateKeyPath: NullIfEmpty(privateKey.Text),
            TrustListStorePath: NullIfEmpty(trustList.Text),
            IssuerListStorePath: NullIfEmpty(issuerList.Text),
            HttpsCertificatePublicKeyPath: NullIfEmpty(httpsPublic.Text),
            HttpsCertificatePrivateKeyPath: NullIfEmpty(httpsPrivate.Text),
            HttpsTrustListStorePath: NullIfEmpty(httpsTrust.Text),
            HttpsIssuerListStorePath: NullIfEmpty(httpsIssuer.Text),
            PushEndpoint: pushEndpoint);
    }

    private void ApplyContext(
        RegisteredApplicationContext ctx,
        ComboBox typeBox,
        TextBox nameBox, TextBox uriBox, TextBox productUriBox,
        TextBox discoveryBox, TextBox capsBox,
        TextBox certStore, TextBox subject, TextBox publicKey, TextBox privateKey,
        TextBox trustList, TextBox issuerList, TextBox domains,
        TextBox httpsPublic, TextBox httpsPrivate, TextBox httpsTrust, TextBox httpsIssuer,
        TextBox pushUrl, TextBlock pushSecurity)
    {
        typeBox.SelectedIndex = (int)ctx.RegistrationType;
        nameBox.Text = ctx.ApplicationName;
        uriBox.Text = ctx.ApplicationUri;
        productUriBox.Text = ctx.ProductUri;
        discoveryBox.Text = string.Join('\n', ctx.DiscoveryUrls);
        capsBox.Text = string.Join('\n', ctx.ServerCapabilities);
        certStore.Text = ctx.CertificateStorePath ?? string.Empty;
        subject.Text = ctx.CertificateSubjectName ?? string.Empty;
        publicKey.Text = ctx.CertificatePublicKeyPath ?? string.Empty;
        privateKey.Text = ctx.CertificatePrivateKeyPath ?? string.Empty;
        trustList.Text = ctx.TrustListStorePath ?? string.Empty;
        issuerList.Text = ctx.IssuerListStorePath ?? string.Empty;
        domains.Text = ctx.Domains ?? string.Empty;
        httpsPublic.Text = ctx.HttpsCertificatePublicKeyPath ?? string.Empty;
        httpsPrivate.Text = ctx.HttpsCertificatePrivateKeyPath ?? string.Empty;
        httpsTrust.Text = ctx.HttpsTrustListStorePath ?? string.Empty;
        httpsIssuer.Text = ctx.HttpsIssuerListStorePath ?? string.Empty;

        if (ctx.PushEndpoint is { } pe)
        {
            m_pickedPushEndpoint = pe;
            pushUrl.Text = pe.EndpointUrl ?? string.Empty;
            pushSecurity.Text = string.IsNullOrEmpty(pe.SecurityPolicyUri)
                ? pe.SecurityMode.ToString()
                : $"{pe.SecurityMode} / {pe.SecurityPolicyUri}";
        }
        else
        {
            m_pickedPushEndpoint = null;
            pushUrl.Text = string.Empty;
            pushSecurity.Text = "(not picked — Sign & Encrypt will be auto-selected if you skip)";
        }
    }

    private static void UpdateModePanels(ComboBox typeBox, StackPanel pullPanel, StackPanel pushPanel)
    {
        GdsRegistrationType mode = ParseRegistrationType(typeBox.SelectedIndex);
        bool isPush = mode == GdsRegistrationType.ServerPush;
        pullPanel.IsVisible = !isPush;
        pushPanel.IsVisible = isPush;
    }

    private static GdsRegistrationType ParseRegistrationType(int index)
    {
        return index switch
        {
            0 => GdsRegistrationType.ClientPull,
            1 => GdsRegistrationType.ServerPull,
            2 => GdsRegistrationType.ServerPush,
            _ => GdsRegistrationType.ClientPull
        };
    }

    private static string SuggestFileName(RegisteredApplicationContext ctx)
    {
        string baseName = !string.IsNullOrWhiteSpace(ctx.ApplicationName)
            ? ctx.ApplicationName
            : (!string.IsNullOrWhiteSpace(ctx.ApplicationUri)
                ? ctx.ApplicationUri
                : "RegisteredApplication");
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(c, '_');
        }
        return baseName + ".json";
    }

    private static List<string> SplitLines(string? text)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return list;
        }

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim().TrimEnd('\r');
            if (!string.IsNullOrEmpty(trimmed))
            {
                list.Add(trimmed);
            }
        }
        return list;
    }

    private static List<string> SplitTokens(string? text)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return list;
        }

        foreach (string token in text.Split([',', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrEmpty(token))
            {
                list.Add(token);
            }
        }
        return list;
    }

    private static string? NullIfEmpty(string? text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
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
