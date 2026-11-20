/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Views;

namespace UaLens.Plugins.RoleManagement;

/// <summary>
/// Modal dialog that collects the parameters required by
/// <see cref="Opc.Ua.Client.Roles.IRoleManagementClient.AddRoleAsync"/>:
/// a non-empty role name and an optional namespace URI.  Returns a
/// <see cref="Result"/> on OK or <c>null</c> on Cancel.
/// </summary>
internal sealed partial class AddRoleDialog : Window
{
    /// <summary>Captured form values returned to the caller.</summary>
    public sealed record Result(string Name, string? NamespaceUri);

    public AddRoleDialog()
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
        var nsBox = this.RequiredControl<TextBox>("NamespaceBox");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");
        var err = this.RequiredControl<TextBlock>("ErrorLabel");

        ok.Click += (_, _) =>
        {
            string name = (nameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                err.Text = "Role name is required.";
                err.IsVisible = true;
                return;
            }
            string? ns = (nsBox.Text ?? string.Empty).Trim();
            if (ns.Length == 0)
            {
                ns = null;
            }
            Close(new Result(name, ns));
        };

        cancel.Click += (_, _) => Close(null);
    }
}
