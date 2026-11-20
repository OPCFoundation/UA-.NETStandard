// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
