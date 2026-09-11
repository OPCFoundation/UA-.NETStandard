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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client.Roles;
using UaLens.Plugins.RoleManagement;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[NonParallelizable]
public sealed class RoleDialogWorkflowTests
{
    public static IEnumerable<IdentityCriteriaType> CriteriaKinds => Enum.GetValues<IdentityCriteriaType>();

    [Test]
    public void RoleUpdateReplacesRulesInPlaceButKeepsTheOriginalRoleIdentity()
    {
        var oldRule = new IdentityMappingRuleType
        {
            CriteriaType = IdentityCriteriaType.UserName, Criteria = "observer"
        };
        var original = new RoleInfo(new NodeId(7501), new QualifiedName("Observer", 2),
            [oldRule], ["urn:old:one", "urn:old:two"], false,
            [new EndpointType { EndpointUrl = "opc.tcp://old.test" }], true, false);
        var role = new RoleVm(original);
        var identities = role.Identities;
        var applications = role.Applications;
        var endpoints = role.Endpoints;
        var user = new IdentityMappingRuleType { CriteriaType = IdentityCriteriaType.UserName, Criteria = "operator" };
        var anonymous = new IdentityMappingRuleType { CriteriaType = IdentityCriteriaType.Anonymous, Criteria = string.Empty };
        var secured = new EndpointType
        {
            EndpointUrl = "opc.tcp://secure.test:4840",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
        };

        role.UpdateFrom(new RoleInfo(new NodeId(9999), new QualifiedName("Replacement"),
            [user, anonymous], ["urn:new:one", "urn:new:two"], true, [secured], false, true));

        Assert.That(role.RoleId, Is.EqualTo(new NodeId(7501)));
        Assert.That(role.DisplayName, Is.EqualTo("Observer"));
        Assert.That(role.BrowseName.NamespaceIndex, Is.EqualTo(2));
        Assert.That(role.Identities, Is.SameAs(identities));
        Assert.That(role.Applications, Is.SameAs(applications));
        Assert.That(role.Endpoints, Is.SameAs(endpoints));
        Assert.That(role.IdentitiesSnapshot.Select(r => r.Criteria), Is.EqualTo(new[] { "operator", string.Empty }));
        Assert.That(role.Identities[0].CriteriaType, Is.EqualTo(IdentityCriteriaType.UserName));
        Assert.That(role.Identities[1].CriteriaType, Is.EqualTo(IdentityCriteriaType.Anonymous));
        Assert.That(role.Applications, Is.EqualTo(s_roleUpdateReplacesRulesInPlaceButKeepsTheOriginalRoleIdentityExpected));
        Assert.That(role.Endpoints.Single().SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        Assert.That(role.Endpoints.Single().EndpointUrl, Is.EqualTo("opc.tcp://secure.test:4840"));
        Assert.That(role.ApplicationsExclude, Is.True);
        Assert.That(role.EndpointsExclude, Is.False);
        Assert.That(role.CustomConfiguration, Is.True);
        Assert.That(original.Applications, Is.EqualTo(s_roleUpdateReplacesRulesInPlaceButKeepsTheOriginalRoleIdentityExpected2));
        Assert.That(original.Identities.Single().Criteria, Is.EqualTo("observer"));

        role.UpdateFrom(new RoleInfo(role.RoleId, role.BrowseName, [], [], false, [], true, false));
        Assert.That(role.Identities, Is.Empty);
        Assert.That(role.Applications, Is.Empty);
        Assert.That(role.Endpoints, Is.Empty);
        Assert.That(role.ApplicationsExclude, Is.False);
        Assert.That(role.EndpointsExclude, Is.True);
        Assert.That(role.CustomConfiguration, Is.False);
        Assert.That(role.RoleId, Is.EqualTo(new NodeId(7501)));
    }

    [Test]
    public async Task RoleSelectionDrivesCommandAvailabilityAndDisconnectClearsTheSnapshot()
    {
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new RoleManagementPlugin(context.Host);
        var first = new RoleVm(new RoleInfo(new NodeId(7511), new QualifiedName("Observer"),
            [], ["urn:observer"], false, [], false, false));
        var second = new RoleVm(new RoleInfo(new NodeId(7512), new QualifiedName("Operator"),
            [], ["urn:operator"], true, [], true, true));
        plugin.Roles.Add(first);
        plugin.Roles.Add(second);
        Assert.That(plugin.RemoveRoleCommand.CanExecute(null), Is.False);
        plugin.SelectedRole = second;
        Assert.That(plugin.RemoveRoleCommand.CanExecute(null), Is.True);
        Assert.That(plugin.AddIdentityCommand.CanExecute(null), Is.True);
        Assert.That(plugin.ToggleApplicationsExcludeCommand.CanExecute(null), Is.True);
        Assert.That(plugin.ToggleEndpointsExcludeCommand.CanExecute(null), Is.True);

        await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.That(plugin.Roles, Is.Empty);
        Assert.That(plugin.SelectedRole, Is.Null);
        Assert.That(plugin.HasSelectedRole, Is.False);
        Assert.That(plugin.RemoveRoleCommand.CanExecute(null), Is.False);
        Assert.That(plugin.AddIdentityCommand.CanExecute(null), Is.False);
        Assert.That(plugin.Status, Is.EqualTo("● Not connected"));
        Assert.That(second.Applications, Is.EqualTo(s_roleSelectionDrivesCommandAvailabilityAndDisconnectClearsTheSExpected));
        Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
        Assert.That(context.ConnectionContext.Discoveries, Is.Empty);
    }

    [TestCase("  urn:roles:factory  ", "urn:roles:factory")]
    [TestCase("  ", null)]
    [Platform("Win,Linux")]
    public Task AddRoleRejectsBlankNameThenReturnsTrimmedNameAndOptionalNamespace(string ns, string? expected)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new AddRoleDialog();
            Task<AddRoleDialog.Result?> prompt = dialog.ShowDialog<AddRoleDialog.Result?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "NameBox").Text = " \t ";
                Click(dialog, "OkButton");
                Assert.That(prompt.IsCompleted, Is.False);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").Text,
                    Is.EqualTo("Role name is required."));
                DesktopInteraction.Control<TextBox>(dialog, "NameBox").Text = "  Operators  ";
                DesktopInteraction.Control<TextBox>(dialog, "NamespaceBox").Text = ns;
                Click(dialog, "OkButton");
                AddRoleDialog.Result result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.Name, Is.EqualTo("Operators"));
                Assert.That(result.NamespaceUri, Is.EqualTo(expected));
                Assert.That(dialog.IsVisible, Is.False);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCaseSource(nameof(CriteriaKinds))]
    [Platform("Win,Linux")]
    public Task IdentityFormValidatesEachCriterionAndReturnsTheSelectedDiscriminator(IdentityCriteriaType kind)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new AddIdentityDialog();
            Task<IdentityMappingRuleType?> prompt =
                dialog.ShowDialog<IdentityMappingRuleType?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<ComboBox>(dialog, "CriteriaTypeBox").SelectedItem = kind;
                DesktopInteraction.Control<TextBox>(dialog, "CriteriaBox").Text = "  ";
                Click(dialog, "OkButton");
                bool allowsEmpty = kind is IdentityCriteriaType.Anonymous or IdentityCriteriaType.AuthenticatedUser;
                if (!allowsEmpty)
                {
                    Assert.That(prompt.IsCompleted, Is.False);
                    Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").Text,
                        Is.EqualTo($"Criteria is required for {kind}."));
                    DesktopInteraction.Control<TextBox>(dialog, "CriteriaBox").Text = "  fixture-criterion  ";
                    Click(dialog, "OkButton");
                }
                IdentityMappingRuleType result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.CriteriaType, Is.EqualTo(kind));
                Assert.That(result.Criteria, Is.EqualTo(allowsEmpty ? string.Empty : "fixture-criterion"));
                Assert.That(dialog.IsVisible, Is.False);
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
    public Task MissingIdentityDiscriminatorCannotBeAcceptedAndCancelReturnsNoRule()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new AddIdentityDialog();
            Task<IdentityMappingRuleType?> prompt =
                dialog.ShowDialog<IdentityMappingRuleType?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<ComboBox>(dialog, "CriteriaTypeBox").SelectedIndex = -1;
                DesktopInteraction.Control<TextBox>(dialog, "CriteriaBox").Text = "operator";
                Click(dialog, "OkButton");
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").Text,
                    Is.EqualTo("Pick a criteria type."));
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

    [TestCase(MessageSecurityMode.None)]
    [TestCase(MessageSecurityMode.Sign)]
    [TestCase(MessageSecurityMode.SignAndEncrypt)]
    [Platform("Win,Linux")]
    public Task EndpointFormRequiresBothUrlAndModeAndPreservesTheChosenSecurityFields(MessageSecurityMode mode)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new AddEndpointDialog();
            Task<EndpointType?> prompt = dialog.ShowDialog<EndpointType?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "EndpointUrlBox").Text = " ";
                Click(dialog, "OkButton");
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").Text,
                    Is.EqualTo("Endpoint URL is required."));
                DesktopInteraction.Control<TextBox>(dialog, "EndpointUrlBox").Text = "  opc.tcp://cell.test:4841  ";
                DesktopInteraction.Control<ComboBox>(dialog, "SecurityModeBox").SelectedIndex = -1;
                Click(dialog, "OkButton");
                Assert.That(prompt.IsCompleted, Is.False);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").Text,
                    Is.EqualTo("Pick a security mode."));
                DesktopInteraction.Control<ComboBox>(dialog, "SecurityModeBox").SelectedItem = mode;
                DesktopInteraction.Control<TextBox>(dialog, "SecurityPolicyBox").Text =
                    mode == MessageSecurityMode.None ? " " : "  " + SecurityPolicies.Basic256Sha256 + "  ";
                DesktopInteraction.Control<TextBox>(dialog, "TransportProfileBox").Text = "  urn:fixture:transport  ";
                Click(dialog, "OkButton");
                EndpointType result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.EndpointUrl, Is.EqualTo("opc.tcp://cell.test:4841"));
                Assert.That(result.SecurityMode, Is.EqualTo(mode));
                Assert.That(result.SecurityPolicyUri,
                    Is.EqualTo(mode == MessageSecurityMode.None ? string.Empty : SecurityPolicies.Basic256Sha256));
                Assert.That(result.TransportProfileUri, Is.EqualTo("urn:fixture:transport"));
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
    public Task ApplicationUriRejectsBlankThenReturnsTheTrimmedNonemptyValue()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new AddApplicationUriDialog();
            Task<string?> prompt = dialog.ShowDialog<string?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "UriBox").Text = "\t";
                Click(dialog, "OkButton");
                Assert.That(prompt.IsCompleted, Is.False);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").Text,
                    Is.EqualTo("Application URI is required."));
                DesktopInteraction.Control<TextBox>(dialog, "UriBox").Text = "  urn:factory:assembler  ";
                Click(dialog, "OkButton");
                Assert.That(await prompt.ConfigureAwait(true), Is.EqualTo("urn:factory:assembler"));
                Assert.That(dialog.IsVisible, Is.False);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase("role")]
    [TestCase("application")]
    [TestCase("endpoint")]
    [Platform("Win,Linux")]
    public Task CancelRoleApplicationAndEndpointFormsDoesNotPublishEditedInput(string form)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            Window dialog = form switch
            {
                "role" => new AddRoleDialog(),
                "application" => new AddApplicationUriDialog(),
                _ => new AddEndpointDialog()
            };
            Task<object?> prompt = dialog.ShowDialog<object?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                string name = form switch { "role" => "NameBox", "application" => "UriBox", _ => "EndpointUrlBox" };
                DesktopInteraction.Control<TextBox>(dialog, name).Text = "discarded candidate";
                Assert.That(prompt.IsCompleted, Is.False);
                Click(dialog, "CancelButton");
                Assert.That(await prompt.ConfigureAwait(true), Is.Null);
                Assert.That(dialog.IsVisible, Is.False);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase("accept", true)]
    [TestCase("cancel", false)]
    [TestCase("close", null)]
    [Platform("Win,Linux")]
    public Task RoleConfirmationReturnsOnlyTheActualUserChoice(string action, bool? expected)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new ConfirmDialog(
                "Remove operator role", "Remove Operators?\nThis cannot be undone.", "Remove");
            Task<bool?> prompt = dialog.ShowDialog<bool?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                Assert.That(dialog.Title, Is.EqualTo("Remove operator role"));
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "MessageLabel").Text,
                    Is.EqualTo("Remove Operators?\nThis cannot be undone."));
                Assert.That(DesktopInteraction.Control<Button>(dialog, "OkButton").Content, Is.EqualTo("Remove"));
                Assert.That(prompt.IsCompleted, Is.False);
                if (action == "close")
                {
                    dialog.Close();
                }
                else
                {
                    Click(dialog, action == "accept" ? "OkButton" : "CancelButton");
                }
                Assert.That(await prompt.ConfigureAwait(true), Is.EqualTo(expected));
                Assert.That(dialog.IsVisible, Is.False);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    private static void Click(Window dialog, string name)
    {
        DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, name));
    }

    private static readonly string[] s_roleUpdateReplacesRulesInPlaceButKeepsTheOriginalRoleIdentityExpected =
    [
        "urn:new:one",
        "urn:new:two",
    ];
    private static readonly string[] s_roleUpdateReplacesRulesInPlaceButKeepsTheOriginalRoleIdentityExpected2 =
    [
        "urn:old:one",
        "urn:old:two",
    ];
    private static readonly string[] s_roleSelectionDrivesCommandAvailabilityAndDisconnectClearsTheSExpected =
    [
        "urn:operator",
    ];
}
