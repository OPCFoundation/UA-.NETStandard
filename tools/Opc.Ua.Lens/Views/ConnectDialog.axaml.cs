// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Views;

/// <summary>
/// Forward-compatible contract every <see cref="ConnectDialog"/> tab
/// content control implements.  Today only the URL tab exists; future
/// MDNS, LDS, and reverse-connect tabs will conform to this so the
/// dialog's <c>Connect</c> click handler stays uniform.
/// </summary>
internal interface IConnectionSourceTab
{
    /// <summary>Returns the endpoint URL the user has chosen, or <c>null</c>
    /// when the source has no usable selection yet.</summary>
    string? GetEndpointUrl();
}

/// <summary>
/// Modal "Connect to OPC UA server" dialog.  Replaces the inline
/// right-column connection panel; reachable from <c>Session → Create…</c>
/// (Ctrl+N).  The dialog body is a <see cref="TabControl"/> with a
/// single <c>URL</c> tab today, designed to grow with MDNS / LDS /
/// reverse-connect sources — see the XAML comment in
/// <c>ConnectDialog.axaml</c>.
/// </summary>
internal sealed partial class ConnectDialog : Window
{
    /// <summary>
    /// The chosen endpoint URL, populated when the user clicks
    /// <c>Connect</c>.  Null when the user cancels.
    /// </summary>
    public string? Result { get; private set; }

    public ConnectDialog(string? defaultUrl)
    {
        InitializeComponent();
        var urlBox = this.RequiredControl<TextBox>("EndpointUrlBox");
        var connect = this.RequiredControl<Button>("ConnectButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        urlBox.Text = defaultUrl ?? string.Empty;
        urlBox.SelectAll();
        urlBox.Focus();

        connect.Click += (_, _) =>
        {
            // Pull the URL from whichever tab is active.  The URL tab's
            // content control implements IConnectionSourceTab via a
            // lightweight adapter built ad-hoc below; future tabs can
            // wrap their content in a UserControl that implements the
            // interface directly so this path stays uniform.
            string? pick = GetActiveSourceUrl();
            if (string.IsNullOrWhiteSpace(pick))
            {
                Title = "Connect to OPC UA server — enter a URL";
                urlBox.Focus();
                return;
            }
            Result = pick.Trim();
            Close(Result);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private string? GetActiveSourceUrl()
    {
        var tabs = this.RequiredControl<TabControl>("SourcesTabs");
        object? activeContent = (tabs.SelectedItem as TabItem)?.Content;
        if (activeContent is IConnectionSourceTab source)
        {
            return source.GetEndpointUrl();
        }
        // Default URL tab — read the EndpointUrlBox by name.
        if (activeContent is Control)
        {
            var box = this.FindControl<TextBox>("EndpointUrlBox");
            return box?.Text;
        }
        return null;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
