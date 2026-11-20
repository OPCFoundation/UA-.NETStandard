// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Views;

namespace UaLens.Plugins.Historian;

/// <summary>
/// Modal window hosting the HistoryUpdate editor previously shown inline
/// in <see cref="HistorianView"/> Row 3. Bound directly to the owning
/// <see cref="HistorianPlugin"/> via DataContext so all the existing
/// per-op bindings (timestamp / value / start / end / at-time / result)
/// continue to work unchanged.  Execute stays inside the dialog and
/// triggers the plug-in's <see cref="HistorianPlugin.ExecuteUpdateCommand"/>;
/// Close just dismisses the window — the plug-in tracks its lifetime
/// so a second click on "History update…" focuses the existing window
/// instead of opening a duplicate.
/// </summary>
internal sealed partial class HistoryUpdateDialog : Window
{
    public HistoryUpdateDialog()
    {
        InitializeComponent();
        var close = this.RequiredControl<Button>("CloseButton");
        close.Click += (_, _) => Close();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
