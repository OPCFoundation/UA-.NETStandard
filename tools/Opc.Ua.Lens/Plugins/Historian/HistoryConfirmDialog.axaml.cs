/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
