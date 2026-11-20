/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Views;

namespace UaLens.Plugins.Performance;

/// <summary>
/// Modal "Performance settings" dialog hosting the workload editor that
/// used to live inline in <see cref="PerformanceView"/>'s top row.
/// DataContext = the owning <see cref="PerformancePlugin"/> so every
/// binding (rate slider, duration, value generator, concurrency hint)
/// continues to work unchanged. Close just dismisses; the plug-in's
/// state is mutated live through TwoWay bindings.
/// </summary>
internal sealed partial class PerformanceSettingsDialog : Window
{
    public PerformanceSettingsDialog()
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
