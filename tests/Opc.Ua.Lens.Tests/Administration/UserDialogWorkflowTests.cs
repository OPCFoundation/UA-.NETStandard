/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client.UserManagement;
using UaLens.Plugins.UserManagement;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[NonParallelizable]
public sealed class UserDialogWorkflowTests
{
    [TestCase(UserConfigurationMask.None, "active", "", false, false, false)]
    [TestCase(UserConfigurationMask.Disabled, "disabled", "", false, false, false)]
    [TestCase(UserConfigurationMask.MustChangePassword, "active", "MustChangePassword", true, false, false)]
    [TestCase(UserConfigurationMask.NoDelete, "active", "NoDelete", false, true, false)]
    [TestCase(UserConfigurationMask.NoChangeByUser, "active", "NoChangeByUser", false, false, true)]
    [TestCase(UserConfigurationMask.Disabled | UserConfigurationMask.MustChangePassword |
        UserConfigurationMask.NoDelete | UserConfigurationMask.NoChangeByUser,
        "disabled", "MustChangePassword, NoDelete, NoChangeByUser", true, true, true)]
    [TestCase((UserConfigurationMask)0x40000000, "active", "", false, false, false)]
    public void UserProjectionPreservesUnknownBitsAndFormatsKnownFlagsInTheDocumentedOrder(
        UserConfigurationMask mask, string state, string flags, bool mustChange, bool noDelete, bool noChange)
    {
        var row = new UserVm(new UserManagementUser("operator", mask, null));
        Assert.That(row.UserName, Is.EqualTo("operator"));
        Assert.That(row.Description, Is.Empty);
        Assert.That(row.UserConfiguration, Is.EqualTo(mask));
        Assert.That(row.StateText, Is.EqualTo(state));
        Assert.That(row.IsActive, Is.EqualTo(state == "active"));
        Assert.That(row.FlagsText, Is.EqualTo(flags));
        Assert.That(row.MustChangePassword, Is.EqualTo(mustChange));
        Assert.That(row.NoDelete, Is.EqualTo(noDelete));
        Assert.That(row.NoChangeByUser, Is.EqualTo(noChange));
    }

    [Test]
    public async Task DisconnectClearsUsersSelectionAndPasswordRestrictionsWithoutModifyingBorrowedRows()
    {
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new UserManagementPlugin(context.Host);
        var observer = new UserVm(new UserManagementUser("observer", 0, "Observer account"));
        var operatorUser = new UserVm(
            new UserManagementUser("operator", UserConfigurationMask.NoDelete, "Operator account"));
        plugin.Users.Add(observer);
        plugin.Users.Add(operatorUser);
        plugin.SelectedUser = operatorUser;
        plugin.PasswordRestrictionsText = "Server policy from the previous connection";

        await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.That(plugin.Users, Is.Empty);
        Assert.That(plugin.SelectedUser, Is.Null);
        Assert.That(plugin.PasswordRestrictionsText, Is.EqualTo("(unknown — connect and refresh)"));
        Assert.That(plugin.Status, Is.EqualTo("● Not connected"));
        Assert.That(operatorUser.UserName, Is.EqualTo("operator"));
        Assert.That(operatorUser.Description, Is.EqualTo("Operator account"));
        Assert.That(operatorUser.NoDelete, Is.True);
        await plugin.ModifyUserAsync(null).ConfigureAwait(false);
        Assert.That(plugin.Status, Is.EqualTo("● Pick a user first."));
        Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
        Assert.That(context.ConnectionContext.Discoveries, Is.Empty);
    }

    [TestCase(UserConfigurationMask.None, false)]
    [TestCase(UserConfigurationMask.MustChangePassword | UserConfigurationMask.NoDelete, true)]
    [TestCase(UserConfigurationMask.Disabled | UserConfigurationMask.NoChangeByUser, true)]
    [Platform("Win,Linux")]
    public Task AddUserTrimsOnlyTheNameAndReturnsTheExactPasswordDescriptionAndFlags(
        UserConfigurationMask mask, bool description)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            string password = NewPassword();
            var dialog = new AddUserDialog();
            Task<AddUserDialogResult?> prompt = dialog.ShowDialog<AddUserDialogResult?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "UserNameBox").Text = "  production-operator  ";
                DesktopInteraction.Control<TextBox>(dialog, "PasswordBox").Text = password;
                DesktopInteraction.Control<TextBox>(dialog, "DescriptionBox").Text =
                    description ? "  Shift B operator  " : " \t ";
                SetMask(dialog, mask);
                Assert.That(prompt.IsCompleted, Is.False);
                Click(dialog, "OkButton");
                AddUserDialogResult result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.UserName, Is.EqualTo("production-operator"));
                Assert.That(result.Password, Is.EqualTo(password));
                Assert.That(result.Description, Is.EqualTo(description ? "  Shift B operator  " : null));
                Assert.That(result.Config, Is.EqualTo(mask));
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase("UserNameBox", "User name is required.")]
    [TestCase("PasswordBox", "Password is required.")]
    [Platform("Win,Linux")]
    public Task AddUserRejectsMissingRequiredInputAndCancellationReturnsNoRequest(string field, string error)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new AddUserDialog();
            Task<AddUserDialogResult?> prompt = dialog.ShowDialog<AddUserDialogResult?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "UserNameBox").Text = "operator";
                DesktopInteraction.Control<TextBox>(dialog, "PasswordBox").Text = NewPassword();
                DesktopInteraction.Control<TextBox>(dialog, field).Text =
                    field == "UserNameBox" ? " \t " : string.Empty;
                Click(dialog, "OkButton");
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").Text, Is.EqualTo(error));
                Assert.That(prompt.IsCompleted, Is.False);
                Click(dialog, "CancelButton");
                Assert.That(await prompt.ConfigureAwait(true), Is.Null);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase("password")]
    [TestCase("configuration")]
    [TestCase("description")]
    [TestCase("all")]
    [Platform("Win,Linux")]
    public Task ModifyDistinguishesUnchangedFieldsFromExplicitlyClearedFields(string changed)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            const UserConfigurationMask original = UserConfigurationMask.Disabled | UserConfigurationMask.NoDelete |
                UserConfigurationMask.MustChangePassword | UserConfigurationMask.NoChangeByUser;
            string password = NewPassword();
            var dialog = new ModifyUserDialog("current-operator", original);
            Task<ModifyUserDialogResult?> prompt =
                dialog.ShowDialog<ModifyUserDialogResult?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                TextBox user = DesktopInteraction.Control<TextBox>(dialog, "UserNameBox");
                Assert.That(user.IsReadOnly, Is.True);
                Assert.That(user.Text, Is.EqualTo("current-operator"));
                Assert.That(DesktopInteraction.Control<CheckBox>(dialog, "DisabledBox").IsChecked, Is.True);
                Assert.That(DesktopInteraction.Control<CheckBox>(dialog, "MustChangePasswordBox").IsChecked, Is.True);
                Assert.That(DesktopInteraction.Control<CheckBox>(dialog, "NoChangeByUserBox").IsChecked, Is.True);
                Assert.That(DesktopInteraction.Control<CheckBox>(dialog, "NoDeleteBox").IsChecked, Is.True);
                bool changePassword = changed is "password" or "all";
                bool changeConfig = changed is "configuration" or "all";
                bool changeDescription = changed is "description" or "all";
                DesktopInteraction.Control<CheckBox>(dialog, "ChangePasswordBox").IsChecked = changePassword;
                DesktopInteraction.Control<CheckBox>(dialog, "ChangeConfigBox").IsChecked = changeConfig;
                DesktopInteraction.Control<CheckBox>(dialog, "ChangeDescriptionBox").IsChecked = changeDescription;
                DesktopInteraction.Control<TextBox>(dialog, "NewPasswordBox").Text = password;
                DesktopInteraction.Control<TextBox>(dialog, "DescriptionBox").Text = string.Empty;
                SetMask(dialog, 0);
                Click(dialog, "OkButton");
                ModifyUserDialogResult result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.NewPassword, Is.EqualTo(changePassword ? password : null));
                Assert.That(result.Config, Is.EqualTo(changeConfig ? (UserConfigurationMask?)0 : null));
                Assert.That(result.Description, Is.EqualTo(changeDescription ? string.Empty : null));
                Assert.That(user.Text, Is.EqualTo("current-operator"));
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task ModifyRejectsNoChangeAndEmptyOptedInPasswordThenAllowsAnExplicitReplacement()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new ModifyUserDialog("operator", UserConfigurationMask.NoDelete);
            Task<ModifyUserDialogResult?> prompt =
                dialog.ShowDialog<ModifyUserDialogResult?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                Click(dialog, "OkButton");
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").Text,
                    Is.EqualTo("Nothing to change — tick at least one section."));
                Assert.That(prompt.IsCompleted, Is.False);
                DesktopInteraction.Control<CheckBox>(dialog, "ChangePasswordBox").IsChecked = true;
                Click(dialog, "OkButton");
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").Text,
                    Is.EqualTo("Enter a new password or untick \"Change password\"."));
                Assert.That(prompt.IsCompleted, Is.False);
                string password = NewPassword();
                DesktopInteraction.Control<TextBox>(dialog, "NewPasswordBox").Text = password;
                Click(dialog, "OkButton");
                ModifyUserDialogResult result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.NewPassword, Is.EqualTo(password));
                Assert.That(result.Config, Is.Null);
                Assert.That(result.Description, Is.Null);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task CancelModifyDoesNotPublishChangedConfigurationOrDescription()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var user = new UserVm(new UserManagementUser("operator", UserConfigurationMask.NoDelete, "Original"));
            var dialog = new ModifyUserDialog(user.UserName, user.UserConfiguration);
            Task<ModifyUserDialogResult?> prompt =
                dialog.ShowDialog<ModifyUserDialogResult?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<CheckBox>(dialog, "ChangeConfigBox").IsChecked = true;
                DesktopInteraction.Control<CheckBox>(dialog, "ChangeDescriptionBox").IsChecked = true;
                DesktopInteraction.Control<TextBox>(dialog, "DescriptionBox").Text = "Discard";
                SetMask(dialog, UserConfigurationMask.Disabled);
                Click(dialog, "CancelButton");
                Assert.That(await prompt.ConfigureAwait(true), Is.Null);
                Assert.That(user.Description, Is.EqualTo("Original"));
                Assert.That(user.IsActive, Is.True);
                Assert.That(user.NoDelete, Is.True);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase("OldPasswordBox")]
    [TestCase("NewPasswordBox")]
    [TestCase("ConfirmPasswordBox")]
    [Platform("Win,Linux")]
    public Task PasswordChangeRequiresAllThreeValuesAndReturnsUntrimmedPasswords(string missing)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            string oldPassword = NewPassword();
            string newPassword = NewPassword();
            var dialog = new ChangePasswordDialog();
            Task<ChangePasswordDialogResult?> prompt =
                dialog.ShowDialog<ChangePasswordDialogResult?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                Button ok = DesktopInteraction.Control<Button>(dialog, "OkButton");
                Assert.That(ok.IsEnabled, Is.False);
                await DesktopInteraction.ChangedAsync(ok, () => ok.IsEnabled, () =>
                {
                    DesktopInteraction.Control<TextBox>(dialog, "OldPasswordBox").Text = oldPassword;
                    DesktopInteraction.Control<TextBox>(dialog, "NewPasswordBox").Text = newPassword;
                    DesktopInteraction.Control<TextBox>(dialog, "ConfirmPasswordBox").Text = newPassword;
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                TextBox field = DesktopInteraction.Control<TextBox>(dialog, missing);
                await DesktopInteraction.ChangedAsync(ok, () => !ok.IsEnabled, () =>
                {
                    field.Text = string.Empty;
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Assert.That(prompt.IsCompleted, Is.False);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").IsVisible, Is.False);
                await DesktopInteraction.ChangedAsync(ok, () => ok.IsEnabled, () =>
                {
                    field.Text = missing == "OldPasswordBox" ? oldPassword : newPassword;
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Click(dialog, "OkButton");
                ChangePasswordDialogResult result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.OldPassword, Is.EqualTo(oldPassword));
                Assert.That(result.NewPassword, Is.EqualTo(newPassword));
                Assert.That(result.OldPassword, Is.Not.EqualTo(result.NewPassword));
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task PasswordMismatchCannotBeAcceptedAndCancelReturnsNoPasswordChange()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new ChangePasswordDialog();
            Task<ChangePasswordDialogResult?> prompt =
                dialog.ShowDialog<ChangePasswordDialogResult?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                TextBlock error = DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel");
                await DesktopInteraction.ChangedAsync(error, () => error.IsVisible, () =>
                {
                    DesktopInteraction.Control<TextBox>(dialog, "OldPasswordBox").Text = NewPassword();
                    DesktopInteraction.Control<TextBox>(dialog, "NewPasswordBox").Text = NewPassword();
                    DesktopInteraction.Control<TextBox>(dialog, "ConfirmPasswordBox").Text = NewPassword();
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Assert.That(error.Text, Is.EqualTo("New password and confirmation do not match."));
                Assert.That(DesktopInteraction.Control<Button>(dialog, "OkButton").IsEnabled, Is.False);
                Assert.That(prompt.IsCompleted, Is.False);
                Click(dialog, "CancelButton");
                Assert.That(await prompt.ConfigureAwait(true), Is.Null);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    private static string NewPassword() => "  " + Guid.NewGuid().ToString("N") + "  ";

    private static void SetMask(Window dialog, UserConfigurationMask mask)
    {
        DesktopInteraction.Control<CheckBox>(dialog, "DisabledBox").IsChecked =
            (mask & UserConfigurationMask.Disabled) != 0;
        DesktopInteraction.Control<CheckBox>(dialog, "MustChangePasswordBox").IsChecked =
            (mask & UserConfigurationMask.MustChangePassword) != 0;
        DesktopInteraction.Control<CheckBox>(dialog, "NoChangeByUserBox").IsChecked =
            (mask & UserConfigurationMask.NoChangeByUser) != 0;
        DesktopInteraction.Control<CheckBox>(dialog, "NoDeleteBox").IsChecked =
            (mask & UserConfigurationMask.NoDelete) != 0;
    }

    private static void Click(Window dialog, string name)
    {
        DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, name));
    }
}
