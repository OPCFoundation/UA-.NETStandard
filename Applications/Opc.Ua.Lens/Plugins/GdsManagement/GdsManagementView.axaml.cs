/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.GdsManagement;

/// <summary>
/// Avalonia <see cref="UserControl"/> hosted in a GDS Management tab.
/// The DataContext is the <see cref="GdsManagementPlugin"/>; selection
/// from the apps ListBox is mirrored into the view model via
/// <c>SelectedItem</c> CompiledBinding, so no code-behind selection
/// plumbing is required.
/// </summary>
internal sealed partial class GdsManagementView : UserControl
{
    public GdsManagementView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
