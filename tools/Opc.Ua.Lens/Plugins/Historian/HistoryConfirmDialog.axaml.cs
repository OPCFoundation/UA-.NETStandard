/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace UaLens.Plugins.Historian;

/// <summary>
/// Yes/no confirmation modal shown before a destructive HistoryUpdate (delete,
/// remove or replace). The message states the real scope of the change — the target
/// node and the affected timestamps or range — so the operator sees exactly what will
/// be modified. Returns <c>true</c> on Apply, <c>false</c>/<c>null</c> on Cancel.
/// </summary>
internal sealed partial class HistoryConfirmDialog : Window
{
    /// <summary>
    /// Parameterless constructor for the XAML/design preview only.
    /// </summary>
    public HistoryConfirmDialog()
        : this("Confirm history change", "Apply this change?", "Apply")
    {
    }

    public HistoryConfirmDialog(string title, string message, string applyText = "Apply")
    {
        InitializeComponent();
        Title = title;
        this.RequiredControl<TextBlock>("MessageLabel").Text = message;
        Button ok = this.RequiredControl<Button>("OkButton");
        ok.Content = applyText;
        Button cancel = this.RequiredControl<Button>("CancelButton");
        ok.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
