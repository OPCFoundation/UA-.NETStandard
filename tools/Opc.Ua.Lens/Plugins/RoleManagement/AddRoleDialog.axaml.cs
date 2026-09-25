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
