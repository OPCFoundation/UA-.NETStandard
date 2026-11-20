/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
