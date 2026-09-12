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
/// Minimal yes/no confirmation modal used by the Role Management
/// plug-in for destructive operations (remove role).  Returns
/// <c>true</c> on OK, <c>false</c>/<c>null</c> on Cancel.
/// </summary>
internal sealed partial class ConfirmDialog : Window
{
    /// <summary>Parameterless ctor for XAML / design preview only.</summary>
    public ConfirmDialog()
        : this("Confirm", "Are you sure?", "OK")
    {
    }

    public ConfirmDialog(string title, string message, string okText = "OK")
    {
        InitializeComponent();
        WireUp(title, message, okText);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireUp(string title, string message, string okText)
    {
        Title = title;
        var label = this.RequiredControl<TextBlock>("MessageLabel");
        label.Text = message;
        var ok = this.RequiredControl<Button>("OkButton");
        ok.Content = okText;
        var cancel = this.RequiredControl<Button>("CancelButton");

        ok.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);
    }
}
