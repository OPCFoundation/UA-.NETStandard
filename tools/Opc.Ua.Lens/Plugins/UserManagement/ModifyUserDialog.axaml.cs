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
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Plugins.UserManagement;

/// <summary>
/// Result returned by <see cref="ModifyUserDialog.ShowDialog(Window)"/>
/// when the user accepts the dialog.  A <c>null</c> value for any field
/// means "leave the server-side value unchanged" — mapped 1:1 onto the
/// nullable parameters of
/// <see cref="Opc.Ua.Client.UserManagement.IUserManagementClient.ModifyUserAsync"/>.
/// </summary>
internal sealed record ModifyUserDialogResult(
    string? NewPassword,
    UserConfigurationMask? Config,
    string? Description);

/// <summary>
/// Modal dialog that gathers the parameters for a
/// <c>ModifyUser</c> call (Part 18 §5.2.4).  Each editable field is
/// gated by a "Change …" check-box so the user can opt-in per field;
/// unchecked sections produce <c>null</c> in the result, which the
/// SDK interprets as "do not modify".
/// </summary>
internal sealed partial class ModifyUserDialog : Window
{
    /// <summary>Parameterless ctor for XAML / design preview only.</summary>
    public ModifyUserDialog()
        : this(userName: string.Empty, currentConfig: 0)
    {
    }

    /// <param name="userName">User-name shown in the read-only field.</param>
    /// <param name="currentConfig">Existing configuration mask used to
    /// seed the four check-boxes inside the Configuration section.</param>
    public ModifyUserDialog(string userName, UserConfigurationMask currentConfig)
    {
        InitializeComponent();
        WireUp(userName, currentConfig);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireUp(string userName, UserConfigurationMask currentConfig)
    {
        TextBox userNameBox = this.RequiredControl<TextBox>("UserNameBox");
        CheckBox changePasswordBox = this.RequiredControl<CheckBox>("ChangePasswordBox");
        TextBox newPasswordBox = this.RequiredControl<TextBox>("NewPasswordBox");
        CheckBox changeConfigBox = this.RequiredControl<CheckBox>("ChangeConfigBox");
        CheckBox disabledBox = this.RequiredControl<CheckBox>("DisabledBox");
        CheckBox mustChangeBox = this.RequiredControl<CheckBox>("MustChangePasswordBox");
        CheckBox noChangeBox = this.RequiredControl<CheckBox>("NoChangeByUserBox");
        CheckBox noDeleteBox = this.RequiredControl<CheckBox>("NoDeleteBox");
        CheckBox changeDescBox = this.RequiredControl<CheckBox>("ChangeDescriptionBox");
        TextBox descriptionBox = this.RequiredControl<TextBox>("DescriptionBox");
        TextBlock errorLabel = this.RequiredControl<TextBlock>("ErrorLabel");
        Button okButton = this.RequiredControl<Button>("OkButton");
        Button cancelButton = this.RequiredControl<Button>("CancelButton");

        userNameBox.Text = userName;

        // Seed the config flags from the existing user so an
        // accidentally-toggled "Change configuration" doesn't reset
        // every other flag back to zero on the server.
        disabledBox.IsChecked = (currentConfig & UserConfigurationMask.Disabled) != 0;
        mustChangeBox.IsChecked = (currentConfig & UserConfigurationMask.MustChangePassword) != 0;
        noChangeBox.IsChecked = (currentConfig & UserConfigurationMask.NoChangeByUser) != 0;
        noDeleteBox.IsChecked = (currentConfig & UserConfigurationMask.NoDelete) != 0;

        okButton.Click += (_, _) =>
        {
            string? newPassword = null;
            if (changePasswordBox.IsChecked == true)
            {
                newPassword = newPasswordBox.Text ?? string.Empty;
                if (string.IsNullOrEmpty(newPassword))
                {
                    errorLabel.Text = "Enter a new password or untick \"Change password\".";
                    errorLabel.IsVisible = true;
                    return;
                }
            }

            UserConfigurationMask? cfg = null;
            if (changeConfigBox.IsChecked == true)
            {
                UserConfigurationMask m = 0;
                if (disabledBox.IsChecked == true)
                {
                    m |= UserConfigurationMask.Disabled;
                }
                if (mustChangeBox.IsChecked == true)
                {
                    m |= UserConfigurationMask.MustChangePassword;
                }
                if (noChangeBox.IsChecked == true)
                {
                    m |= UserConfigurationMask.NoChangeByUser;
                }
                if (noDeleteBox.IsChecked == true)
                {
                    m |= UserConfigurationMask.NoDelete;
                }
                cfg = m;
            }

            string? description = null;
            if (changeDescBox.IsChecked == true)
            {
                // Convention: empty string clears the description.
                description = descriptionBox.Text ?? string.Empty;
            }

            if (newPassword is null && cfg is null && description is null)
            {
                errorLabel.Text = "Nothing to change — tick at least one section.";
                errorLabel.IsVisible = true;
                return;
            }

            Close(new ModifyUserDialogResult(newPassword, cfg, description));
        };
        cancelButton.Click += (_, _) => Close(null);
    }
}
