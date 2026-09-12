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
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Views;

namespace UaLens.Plugins.GdsDiscovery;

/// <summary>
/// Edits a <see cref="QueryServersFilter"/> in place. Returns the
/// edited filter on OK or <c>null</c> on cancel.
/// </summary>
internal sealed partial class QueryServersFilterDialog : Window
{
    public QueryServersFilterDialog(QueryServersFilter initial)
    {
        InitializeComponent();
        var appName = this.RequiredControl<TextBox>("AppNameBox");
        var appUri = this.RequiredControl<TextBox>("AppUriBox");
        var productUri = this.RequiredControl<TextBox>("ProductUriBox");
        var caps = this.RequiredControl<TextBox>("CapabilitiesBox");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        appName.Text = initial.ApplicationName ?? string.Empty;
        appUri.Text = initial.ApplicationUri ?? string.Empty;
        productUri.Text = initial.ProductUri ?? string.Empty;
        caps.Text = string.Join(", ", initial.ServerCapabilities);

        ok.Click += (_, _) =>
        {
            var next = new QueryServersFilter
            {
                ApplicationName = NullIfBlank(appName.Text),
                ApplicationUri = NullIfBlank(appUri.Text),
                ProductUri = NullIfBlank(productUri.Text)
            };
            string raw = (caps.Text ?? string.Empty).Trim();
            if (raw.Length > 0)
            {
                foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = part.Trim();
                    if (trimmed.Length > 0)
                    {
                        next.ServerCapabilities.Add(trimmed);
                    }
                }
            }
            Close(next);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
