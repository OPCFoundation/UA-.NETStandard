/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using Opc.Ua.Gds;
using UaLens.Views;

namespace UaLens.Plugins.GdsManagement;

/// <summary>
/// Modal dialog that collects the fields required by
/// <see cref="GlobalDiscoveryServerClient.RegisterApplicationAsync"/> and
/// returns a populated <see cref="ApplicationRecordDataType"/> on OK.
/// Returns <c>null</c> when the user cancels.  Light client-side
/// validation only — the server is the source of truth on whether the
/// record is accepted.
/// </summary>
internal sealed partial class RegisterApplicationDialog : Window
{
    public RegisterApplicationDialog()
    {
        InitializeComponent();
        WireUp();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireUp()
    {
        var nameBox = this.RequiredControl<TextBox>("NameBox");
        var uriBox = this.RequiredControl<TextBox>("UriBox");
        var productUriBox = this.RequiredControl<TextBox>("ProductUriBox");
        var typeBox = this.RequiredControl<ComboBox>("TypeBox");
        var discoveryBox = this.RequiredControl<TextBox>("DiscoveryBox");
        var capsBox = this.RequiredControl<TextBox>("CapabilitiesBox");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        var err = this.RequiredControl<TextBlock>("ErrorLabel");

        typeBox.SelectedIndex = 0;

        ok.Click += (_, _) =>
        {
            try
            {
                SetError(err, null);
                string name = (nameBox.Text ?? string.Empty).Trim();
                string uri = (uriBox.Text ?? string.Empty).Trim();
                string product = (productUriBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(name))
                {
                    SetError(err, "Application Name is required.");
                    return;
                }
                if (string.IsNullOrEmpty(uri))
                {
                    SetError(err, "Application URI is required.");
                    return;
                }
                if (string.IsNullOrEmpty(product))
                {
                    SetError(err, "Product URI is required.");
                    return;
                }
                ApplicationType appType = ParseType(typeBox.SelectedIndex);
                List<string> discoveryList = SplitLines(discoveryBox.Text);
                List<string> capabilityList = SplitTokens(capsBox.Text);

                var record = new ApplicationRecordDataType
                {
                    ApplicationNames = [new LocalizedText(string.Empty, name)],
                    ApplicationUri = uri,
                    ProductUri = product,
                    ApplicationType = appType,
                    DiscoveryUrls = discoveryList.ToArray(),
                    ServerCapabilities = capabilityList.ToArray()
                };
                Close(record);
            }
            catch (Exception ex)
            {
                SetError(err, $"Validation failed: {ex.Message}");
            }
        };
        cancel.Click += (_, _) => Close(null);
    }

    private static ApplicationType ParseType(int index)
    {
        return index switch
        {
            0 => ApplicationType.Server,
            1 => ApplicationType.Client,
            2 => ApplicationType.ClientAndServer,
            3 => ApplicationType.DiscoveryServer,
            _ => ApplicationType.Server
        };
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
