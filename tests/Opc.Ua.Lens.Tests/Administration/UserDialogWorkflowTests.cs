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
public sealed partial class UserDialogWorkflowTests
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
}
