// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Opc.Ua;
using UaLens.Views;

namespace UaLens.Plugins.UserManagement;

/// <summary>
/// Result returned by <see cref="AddUserDialog.ShowDialog(Window)"/>
/// when the user accepts the dialog.  All four <see cref="UserConfigurationMask"/>
/// bits are conveyed via <see cref="Config"/>; <see cref="Description"/> is
/// <c>null</c> when the user left the field empty.
/// </summary>
internal sealed record AddUserDialogResult(
    string UserName,
    string Password,
    UserConfigurationMask Config,
    string? Description);

/// <summary>
/// Modal dialog that gathers the parameters for an
/// <c>AddUser</c> call (Part 18 §5.2.3) and returns them as an
/// <see cref="AddUserDialogResult"/> on OK, or <c>null</c> on Cancel.
/// </summary>
internal sealed partial class AddUserDialog : Window
{
    public AddUserDialog()
    {
        InitializeComponent();
        WireUp();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireUp()
    {
        TextBox userNameBox = this.RequiredControl<TextBox>("UserNameBox");
        TextBox passwordBox = this.RequiredControl<TextBox>("PasswordBox");
        TextBox descriptionBox = this.RequiredControl<TextBox>("DescriptionBox");
        CheckBox disabledBox = this.RequiredControl<CheckBox>("DisabledBox");
        CheckBox mustChangeBox = this.RequiredControl<CheckBox>("MustChangePasswordBox");
        CheckBox noChangeBox = this.RequiredControl<CheckBox>("NoChangeByUserBox");
        CheckBox noDeleteBox = this.RequiredControl<CheckBox>("NoDeleteBox");
        TextBlock errorLabel = this.RequiredControl<TextBlock>("ErrorLabel");
        Button okButton = this.RequiredControl<Button>("OkButton");
        Button cancelButton = this.RequiredControl<Button>("CancelButton");

        okButton.Click += (_, _) =>
        {
            string userName = userNameBox.Text?.Trim() ?? string.Empty;
            string password = passwordBox.Text ?? string.Empty;
            if (string.IsNullOrEmpty(userName))
            {
                errorLabel.Text = "User name is required.";
                errorLabel.IsVisible = true;
                return;
            }
            if (string.IsNullOrEmpty(password))
            {
                errorLabel.Text = "Password is required.";
                errorLabel.IsVisible = true;
                return;
            }

            UserConfigurationMask cfg = 0;
            if (disabledBox.IsChecked == true)
            {
                cfg |= UserConfigurationMask.Disabled;
            }
            if (mustChangeBox.IsChecked == true)
            {
                cfg |= UserConfigurationMask.MustChangePassword;
            }
            if (noChangeBox.IsChecked == true)
            {
                cfg |= UserConfigurationMask.NoChangeByUser;
            }
            if (noDeleteBox.IsChecked == true)
            {
                cfg |= UserConfigurationMask.NoDelete;
            }

            string? description = descriptionBox.Text;
            if (string.IsNullOrWhiteSpace(description))
            {
                description = null;
            }

            Close(new AddUserDialogResult(userName, password, cfg, description));
        };
        cancelButton.Click += (_, _) => Close(null);
    }
}
