/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UaLens.Views;

namespace UaLens.Plugins.UserManagement;

/// <summary>
/// Result returned by <see cref="ChangePasswordDialog.ShowDialog(Window)"/>
/// when the user accepts the dialog.  The dialog itself enforces that
/// the New and Confirm fields match, so the caller can dispatch the
/// pair directly.
/// </summary>
internal sealed record ChangePasswordDialogResult(
    string OldPassword,
    string NewPassword);

/// <summary>
/// Modal dialog that collects the old and new passwords for a
/// <c>ChangePassword</c> call (Part 18 §5.2.6).  The OK button stays
/// disabled until all three fields are non-empty and the new /
/// confirm fields match.
/// </summary>
internal sealed partial class ChangePasswordDialog : Window
{
    public ChangePasswordDialog()
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
        TextBox oldBox = this.RequiredControl<TextBox>("OldPasswordBox");
        TextBox newBox = this.RequiredControl<TextBox>("NewPasswordBox");
        TextBox confirmBox = this.RequiredControl<TextBox>("ConfirmPasswordBox");
        TextBlock errorLabel = this.RequiredControl<TextBlock>("ErrorLabel");
        Button okButton = this.RequiredControl<Button>("OkButton");
        Button cancelButton = this.RequiredControl<Button>("CancelButton");

        void UpdateOkState()
        {
            string oldP = oldBox.Text ?? string.Empty;
            string newP = newBox.Text ?? string.Empty;
            string confirmP = confirmBox.Text ?? string.Empty;
            bool nonEmpty = !string.IsNullOrEmpty(oldP)
                && !string.IsNullOrEmpty(newP)
                && !string.IsNullOrEmpty(confirmP);
            bool matches = newP == confirmP;
            okButton.IsEnabled = nonEmpty && matches;
            if (nonEmpty && !matches)
            {
                errorLabel.Text = "New password and confirmation do not match.";
                errorLabel.IsVisible = true;
            }
            else
            {
                errorLabel.IsVisible = false;
            }
        }

        oldBox.TextChanged += (_, _) => UpdateOkState();
        newBox.TextChanged += (_, _) => UpdateOkState();
        confirmBox.TextChanged += (_, _) => UpdateOkState();

        okButton.Click += (_, _) =>
        {
            string oldP = oldBox.Text ?? string.Empty;
            string newP = newBox.Text ?? string.Empty;
            string confirmP = confirmBox.Text ?? string.Empty;
            if (newP != confirmP)
            {
                errorLabel.Text = "New password and confirmation do not match.";
                errorLabel.IsVisible = true;
                return;
            }
            Close(new ChangePasswordDialogResult(oldP, newP));
        };
        cancelButton.Click += (_, _) => Close(null);
    }
}
