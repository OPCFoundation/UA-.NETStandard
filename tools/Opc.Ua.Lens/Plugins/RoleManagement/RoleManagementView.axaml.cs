// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace UaLens.Plugins.RoleManagement;

/// <summary>
/// Avalonia view for <see cref="RoleManagementPlugin"/>.  Layout: a
/// top toolbar of role-level actions, a left-hand ListBox of roles and
/// a right-hand TabControl with one tab per editable facet (Identities,
/// Applications, Endpoints, Custom configuration).
/// </summary>
internal sealed partial class RoleManagementView : UserControl
{
    public RoleManagementView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Routes the Custom-config CheckBox toggle through the plugin's
    /// <see cref="RoleManagementPlugin.SetCustomConfigurationCommand"/> so the
    /// SDK write happens before the bound value flips, and the bound
    /// state stays in sync with whatever the server actually accepted.
    /// </summary>
    private void OnCustomConfigChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RoleManagementPlugin vm)
        {
            return;
        }
        if (sender is not CheckBox cb || cb.IsChecked is not bool target)
        {
            return;
        }
        if (vm.SelectedRole is null || vm.SelectedRole.CustomConfiguration == target)
        {
            return;
        }
        if (vm.SetCustomConfigurationCommand.CanExecute(target))
        {
            vm.SetCustomConfigurationCommand.Execute(target);
        }
    }
}
