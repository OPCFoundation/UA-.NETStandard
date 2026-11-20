/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Views;

namespace UaLens.Plugins.RoleManagement;

/// <summary>
/// Single-textbox modal that captures an Application URI string for
/// <see cref="Opc.Ua.Client.Roles.IRoleManagementClient.AddApplicationAsync"/>.
/// Returns the entered URI on OK or <c>null</c> on Cancel.
/// </summary>
internal sealed partial class AddApplicationUriDialog : Window
{
    public AddApplicationUriDialog()
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
        var uriBox = this.RequiredControl<TextBox>("UriBox");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        var err = this.RequiredControl<TextBlock>("ErrorLabel");

        ok.Click += (_, _) =>
        {
            string uri = (uriBox.Text ?? string.Empty).Trim();
            if (uri.Length == 0)
            {
                err.Text = "Application URI is required.";
                err.IsVisible = true;
                return;
            }
            Close(uri);
        };

        cancel.Click += (_, _) => Close(null);
    }
}
