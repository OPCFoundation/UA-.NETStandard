// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Plugins.RoleManagement;

/// <summary>
/// Modal dialog that collects the inputs for
/// <see cref="Opc.Ua.Client.Roles.IRoleManagementClient.AddEndpointAsync"/>.
/// Returns a fully-built <see cref="EndpointType"/> on OK or <c>null</c>
/// on Cancel.
/// </summary>
internal sealed partial class AddEndpointDialog : Window
{
    private static readonly MessageSecurityMode[] s_modes =
    [
        MessageSecurityMode.None,
        MessageSecurityMode.Sign,
        MessageSecurityMode.SignAndEncrypt,
    ];

    public AddEndpointDialog()
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
        var urlBox = this.RequiredControl<TextBox>("EndpointUrlBox");
        var modeBox = this.RequiredControl<ComboBox>("SecurityModeBox");
        var policyBox = this.RequiredControl<TextBox>("SecurityPolicyBox");
        var transportBox = this.RequiredControl<TextBox>("TransportProfileBox");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        var err = this.RequiredControl<TextBlock>("ErrorLabel");

        modeBox.ItemsSource = s_modes;
        modeBox.SelectedIndex = 0;

        ok.Click += (_, _) =>
        {
            string url = (urlBox.Text ?? string.Empty).Trim();
            if (url.Length == 0)
            {
                err.Text = "Endpoint URL is required.";
                err.IsVisible = true;
                return;
            }
            if (modeBox.SelectedItem is not MessageSecurityMode mode)
            {
                err.Text = "Pick a security mode.";
                err.IsVisible = true;
                return;
            }
            var endpoint = new EndpointType
            {
                EndpointUrl = url,
                SecurityMode = mode,
                SecurityPolicyUri = (policyBox.Text ?? string.Empty).Trim(),
                TransportProfileUri = (transportBox.Text ?? string.Empty).Trim()
            };
            Close(endpoint);
        };

        cancel.Click += (_, _) => Close(null);
    }
}
