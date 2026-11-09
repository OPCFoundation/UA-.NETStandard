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
public sealed partial class RoleDialogWorkflowTests
{

    [Test]
    public void RoleUpdateReplacesRulesInPlaceButKeepsTheOriginalRoleIdentity()
    {
        var oldRule = new IdentityMappingRuleType
        {
            CriteriaType = IdentityCriteriaType.UserName,
            Criteria = "observer"
        };
        var original = new RoleInfo(new NodeId(7501), new QualifiedName("Observer", 2),
            [oldRule], ["urn:old:one", "urn:old:two"], false,
            [new EndpointType { EndpointUrl = "opc.tcp://old.test" }], true, false);
        var role = new RoleVm(original);
        var identities = role.Identities;
        var applications = role.Applications;
        var endpoints = role.Endpoints;
        var user = new IdentityMappingRuleType { CriteriaType = IdentityCriteriaType.UserName, Criteria = "operator" };
        var anonymous = new IdentityMappingRuleType
        {
            CriteriaType = IdentityCriteriaType.Anonymous,
            Criteria = string.Empty
        };
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
        Assert.That(
            role.Applications,
            Is.EqualTo(s_roleUpdateReplacesRulesInPlaceButKeepsTheOriginalRoleIdentityExpected));
        Assert.That(role.Endpoints.Single().SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        Assert.That(role.Endpoints.Single().EndpointUrl, Is.EqualTo("opc.tcp://secure.test:4840"));
        Assert.That(role.ApplicationsExclude, Is.True);
        Assert.That(role.EndpointsExclude, Is.False);
        Assert.That(role.CustomConfiguration, Is.True);
        Assert.That(
            original.Applications,
            Is.EqualTo(s_roleUpdateReplacesRulesInPlaceButKeepsTheOriginalRoleIdentityExpected2));
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
        Assert.That(
            second.Applications,
            Is.EqualTo(s_roleSelectionDrivesCommandAvailabilityAndDisconnectClearsTheSExpected));
        Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
        Assert.That(context.ConnectionContext.Discoveries, Is.Empty);
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
