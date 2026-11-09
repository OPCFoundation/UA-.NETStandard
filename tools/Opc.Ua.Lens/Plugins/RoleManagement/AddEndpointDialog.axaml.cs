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
