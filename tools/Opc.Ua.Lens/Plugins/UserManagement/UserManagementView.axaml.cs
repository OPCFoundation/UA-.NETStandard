// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.UserManagement;

/// <summary>
/// Avalonia view for <see cref="UserManagementPlugin"/>.  Layout: a
/// top WrapPanel toolbar, a DataGrid listing users in the body, a
/// password-restrictions panel below the grid, and a single-line
/// status bar at the bottom.
/// </summary>
internal sealed partial class UserManagementView : UserControl
{
    public UserManagementView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
