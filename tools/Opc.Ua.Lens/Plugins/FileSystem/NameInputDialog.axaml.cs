/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Views;

namespace UaLens.Plugins.FileSystem;

/// <summary>
/// Minimal single-line prompt with OK / Cancel. Returns the trimmed
/// entered value (or <c>null</c> when the user cancels). Reused by the
/// FileSystem plug-in for the "New folder" and "Rename" workflows.
/// </summary>
internal sealed partial class NameInputDialog : Window
{
    public NameInputDialog(string title, string prompt, string defaultValue)
    {
        InitializeComponent();
        Title = title;
        var promptLabel = this.RequiredControl<TextBlock>("PromptLabel");
        var nameBox = this.RequiredControl<TextBox>("NameBox");
        var ok = this.RequiredControl<Button>("OkButton");
        var cancel = this.RequiredControl<Button>("CancelButton");

        promptLabel.Text = prompt;
        nameBox.Text = defaultValue;
        nameBox.SelectAll();
        nameBox.Focus();

        ok.Click += (_, _) =>
        {
            string value = nameBox.Text?.Trim() ?? string.Empty;
            Close(string.IsNullOrEmpty(value) ? null : value);
        };
        cancel.Click += (_, _) => Close(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
