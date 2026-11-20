/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.GdsDiscovery;

/// <summary>
/// Avalonia view for the <see cref="GdsDiscoveryPlugin"/>. Layout:
/// toolbar on top, left-hand TreeView of discovery roots, right-hand
/// ListBox of endpoints for the currently selected server.
/// </summary>
internal sealed partial class GdsDiscoveryView : UserControl
{
    public GdsDiscoveryView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
