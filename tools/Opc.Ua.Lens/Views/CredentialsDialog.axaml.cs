/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Views;

internal sealed partial class CredentialsDialog : Window
{
    public CredentialsDialog(string? defaultUsername = null)
    {
        InitializeComponent();
        var u = this.RequiredControl<TextBox>("UsernameBox");
        var p = this.RequiredControl<TextBox>("PasswordBox");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        u.Text = defaultUsername ?? string.Empty;

        ok.Click += (_, _) =>
        {
            Username = u.Text ?? string.Empty;
            Password = p.Text ?? string.Empty;
            Close((Username, Password));
        };
        cancel.Click += (_, _) => Close(null);
        Closed += (_, _) =>
        {
            p.Text = string.Empty;
        };
    }

    public string? Username { get; private set; }

    public string? Password { get; private set; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
