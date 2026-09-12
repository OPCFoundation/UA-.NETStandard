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
