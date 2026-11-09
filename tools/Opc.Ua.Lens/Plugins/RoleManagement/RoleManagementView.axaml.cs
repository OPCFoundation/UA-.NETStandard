/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
