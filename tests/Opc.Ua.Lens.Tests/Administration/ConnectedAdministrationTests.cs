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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client.UserManagement;
using UaLens.Plugins.RoleManagement;
using UaLens.Plugins.UserManagement;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class ConnectedAdministrationTests
{
    [TestCase("present")]
    [TestCase("optional")]
    [TestCase("denied")]
    public Task UserRefreshPublishesTypedAccountsAndReportsOptionalOrMandatoryFailures(string response)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            ConfigureTranslations(context);
            context.Read = (ids, _) =>
            {
                Assert.That(ids.Count, Is.EqualTo(1));
                Assert.That(ids[0].AttributeId, Is.EqualTo(Attributes.Value));
                DataValue value;
                if (ids[0].NodeId == new NodeId(BrowseNames.Users, 2))
                {
                    value = response == "denied"
                        ? DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied)
                        : new DataValue(Variant.FromStructure((ArrayOf<UserManagementDataType>)
                        [
                            new()
                            {
                                UserName = "operator", Description = "Operations",
                                UserConfiguration = (uint)(UserConfigurationMask.Disabled | UserConfigurationMask.NoDelete)
                            },
                            new() { UserName = "observer" }
                        ]));
                }
                else
                {
                    Assert.That(ids[0].NodeId, Is.EqualTo(new NodeId(BrowseNames.PasswordRestrictions, 2)));
                    if (response == "optional")
                    {
                        throw new ServiceResultException(StatusCodes.BadAttributeIdInvalid);
                    }
                    value = new DataValue(Variant.From(new LocalizedText("Use a long passphrase")));
                }
                return ValueTask.FromResult(new ReadResponse { Results = [value] });
            };
            await using var plugin = new UserManagementPlugin(context.Host);
            await plugin.RefreshAsync().ConfigureAwait(true);
            await FlushAsync().ConfigureAwait(true);
            if (response == "denied")
            {
                Assert.That(plugin.Users, Is.Empty);
                Assert.That(plugin.Status, Does.StartWith("● Refresh failed:").And.Contain("BadUserAccessDenied"));
            }
            else
            {
                Assert.That(plugin.Users.Select(user => user.UserName), Is.EqualTo(s_users));
                Assert.That(plugin.Users[0].IsActive, Is.False);
                Assert.That(plugin.Users[0].NoDelete, Is.True);
                Assert.That(plugin.Users[0].Description, Is.EqualTo("Operations"));
                Assert.That(plugin.Users[1].IsActive, Is.True);
                Assert.That(plugin.PasswordRestrictionsText, Is.EqualTo(response == "optional"
                    ? "(server did not expose PasswordRestrictions)" : "Use a long passphrase"));
                Assert.That(plugin.Status, Is.EqualTo("● 2 user(s)"));
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task RoleRefreshPreservesSelectionByIdentityAndDropsMissingRoles(bool removeSelected)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            ConfigureTranslations(context);
            bool empty = false;
            context.Browse = (ids, _) =>
            {
                Assert.That(ids[0].NodeId, Is.EqualTo(ObjectIds.Server_ServerCapabilities_RoleSet));
                return ValueTask.FromResult(new BrowseResponse
                {
                    Results = [new BrowseResult
                    {
                        References = empty ? [] :
                        [
                            new ReferenceDescription
                            {
                                NodeId = s_roleId, TypeDefinition = ObjectTypeIds.RoleType,
                                BrowseName = new QualifiedName("Operators"), NodeClass = NodeClass.Object
                            }
                        ]
                    }]
                });
            };
            context.Read = (ids, _) => ValueTask.FromResult(new ReadResponse
            {
                Results = ids.ToArray()!.Select(id => new DataValue(RoleValue(id))).ToArray()
            });
            await using var plugin = new RoleManagementPlugin(context.Host);
            await plugin.RefreshAsync().ConfigureAwait(true);
            Assert.That(plugin.Roles, Has.Count.EqualTo(1));
            var selected = plugin.Roles[0];
            Assert.That(selected.DisplayName, Is.EqualTo("Operators"));
            Assert.That(selected.Applications, Is.EqualTo(s_applications));
            Assert.That(selected.ApplicationsExclude, Is.False);
            Assert.That(selected.EndpointsExclude, Is.True);
            plugin.SelectedRole = selected;
            empty = removeSelected;
            await plugin.RefreshAsync().ConfigureAwait(true);
            Assert.That(plugin.Roles, Has.Count.EqualTo(removeSelected ? 0 : 1));
            if (removeSelected)
            {
                Assert.That(plugin.SelectedRole, Is.Null);
                Assert.That(plugin.HasSelectedRole, Is.False);
            }
            else
            {
                Assert.That(plugin.SelectedRole, Is.SameAs(plugin.Roles[0]).And.Not.SameAs(selected));
                Assert.That(plugin.SelectedRole!.RoleId, Is.EqualTo(s_roleId));
            }
        });
    }

    [TestCase("ApplicationsExclude", false)]
    [TestCase("EndpointsExclude", false)]
    [TestCase("CustomConfiguration", false)]
    [TestCase("ApplicationsExclude", true)]
    [TestCase("EndpointsExclude", true)]
    [TestCase("CustomConfiguration", true)]
    public Task RoleFlagWritesUseExactPropertiesAndDoNotCommitRejectedChanges(string property, bool reject)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            ConfigureTranslations(context);
            await using var plugin = new RoleManagementPlugin(context.Host);
            var role = new RoleVm(new Opc.Ua.Client.Roles.RoleInfo(
                s_roleId, new QualifiedName("Operators"), [], [], false, [], false, false));
            plugin.Roles.Add(role);
            plugin.SelectedRole = role;
            ArrayOf<WriteValue> sent = default;
            context.Write = (values, _) =>
            {
                sent = values;
                return ValueTask.FromResult(new WriteResponse
                {
                    Results = [reject ? StatusCodes.BadUserAccessDenied : StatusCodes.Good]
                });
            };
            switch (property)
            {
                case "ApplicationsExclude":
                    await plugin.ToggleApplicationsExcludeAsync().ConfigureAwait(true);
                    Assert.That(role.ApplicationsExclude, Is.EqualTo(!reject));
                    break;
                case "EndpointsExclude":
                    await plugin.ToggleEndpointsExcludeAsync().ConfigureAwait(true);
                    Assert.That(role.EndpointsExclude, Is.EqualTo(!reject));
                    break;
                default:
                    await plugin.SetCustomConfigurationAsync(true).ConfigureAwait(true);
                    Assert.That(role.CustomConfiguration, Is.EqualTo(!reject));
                    break;
            }
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].NodeId, Is.EqualTo(new NodeId(property, 2)));
            Assert.That(sent[0].AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(sent[0].Value.WrappedValue, Is.EqualTo(Variant.From(true)));
            Assert.That(plugin.Status, Does.Contain(reject ? "failed:" : "= True"));
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public Task RemovingAUserRequiresConsentAndRetainsTheAccountWhenTheServerRejectsIt(bool accept, bool reject)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            ConfigureTranslations(context);
            context.Read = (ids, _) => ValueTask.FromResult(new ReadResponse
            {
                Results = [new DataValue(ids[0].NodeId == new NodeId(BrowseNames.Users, 2)
                    ? Variant.FromStructure(ArrayOf<UserManagementDataType>.Empty)
                    : Variant.From(new LocalizedText("policy")))]
            });
            ArrayOf<CallMethodRequest> calls = default;
            context.Call = (requests, _) =>
            {
                calls = requests;
                return ValueTask.FromResult(new CallResponse
                {
                    Results = [new CallMethodResult
                    {
                        StatusCode = reject ? StatusCodes.BadUserAccessDenied : StatusCodes.Good,
                        OutputArguments = []
                    }]
                });
            };
            await using var plugin = new UserManagementPlugin(context.Host);
            var user = new UserVm(new UserManagementUser("operator", UserConfigurationMask.None, "Operations"));
            plugin.Users.Add(user);
            plugin.SelectedUser = user;
            Task operation = Task.CompletedTask;
            Window confirmation = await DesktopInteraction.OpenedAsync<Window>(
                () => operation = plugin.RemoveUserAsync(user)).ConfigureAwait(true);
            Assert.That(calls.IsNull, Is.True);
            Button button = confirmation.GetLogicalDescendants().OfType<Button>()
                .Single(value => Equals(value.Content, accept ? "Yes" : "Cancel"));
            DesktopInteraction.Click(button);
            await operation.ConfigureAwait(true);
            await FlushAsync().ConfigureAwait(true);
            if (accept)
            {
                Assert.That(calls.Count, Is.EqualTo(1));
                Assert.That(calls[0].ObjectId, Is.EqualTo(new NodeId(Objects.UserManagement)));
                Assert.That(calls[0].InputArguments.Count, Is.EqualTo(1));
                Assert.That(calls[0].InputArguments[0], Is.EqualTo(Variant.From("operator")));
                Assert.That(plugin.Status, Does.Contain(reject ? "Remove user failed:" : "Removed user 'operator'"));
            }
            else
            {
                Assert.That(calls.IsNull, Is.True);
            }
            Assert.That(plugin.Users, Has.Count.EqualTo(accept && !reject ? 0 : 1));
        });
    }

    private static void ConfigureTranslations(ConnectedProtocolContext context)
    {
        context.Translate = (paths, _) =>
        {
            BrowsePathResult[] result = new BrowsePathResult[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                string name = paths[i].RelativePath.Elements[^1].TargetName.Name
                    ?? throw new AssertionException("A child name is required.");
                result[i] = new BrowsePathResult
                {
                    Targets = [new BrowsePathTarget { TargetId = new NodeId(name, 2), RemainingPathIndex = uint.MaxValue }]
                };
            }
            return ValueTask.FromResult(new TranslateBrowsePathsToNodeIdsResponse { Results = result });
        };
    }

    private static Variant RoleValue(ReadValueId id)
    {
        if (id.AttributeId == Attributes.BrowseName)
        {
            return Variant.From(new QualifiedName("Operators"));
        }
        if (id.NodeId == new NodeId(BrowseNames.Applications, 2))
        {
            return Variant.From((ArrayOf<string>)s_applications);
        }
        if (id.NodeId == new NodeId(BrowseNames.Identities, 2))
        {
            return Variant.FromStructure(ArrayOf<IdentityMappingRuleType>.Empty);
        }
        if (id.NodeId == new NodeId(BrowseNames.Endpoints, 2))
        {
            return Variant.FromStructure(ArrayOf<EndpointType>.Empty);
        }
        return Variant.From(id.NodeId == new NodeId(BrowseNames.EndpointsExclude, 2));
    }

    private static async Task FlushAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static readonly string[] s_users = ["operator", "observer"];
    private static readonly string[] s_applications = ["urn:factory:application"];
    private static readonly NodeId s_roleId = new("Operators", 2);
}
