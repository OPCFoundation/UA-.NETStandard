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
                                UserConfiguration = (uint)(UserConfigurationMask.Disabled |
                                    UserConfigurationMask.NoDelete)
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

    [TestCase("Identity", false, false)]
    [TestCase("Identity", true, false)]
    [TestCase("Identity", false, true)]
    [TestCase("Application", false, false)]
    [TestCase("Application", true, false)]
    [TestCase("Application", false, true)]
    [TestCase("Endpoint", false, false)]
    [TestCase("Endpoint", true, false)]
    [TestCase("Endpoint", false, true)]
    public Task RoleMembershipChangesPreserveTypedArgumentsAndRefreshOnlyConfirmedState(
        string member, bool remove, bool reject)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            ConfigureTranslations(context);
            var identity = new IdentityMappingRuleType
            {
                CriteriaType = IdentityCriteriaType.UserName, Criteria = "operator"
            };
            var endpoint = new EndpointType
            {
                EndpointUrl = "opc.tcp://permitted.test:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                TransportProfileUri = Profiles.UaTcpTransport
            };
            bool present = remove;
            context.Read = (ids, _) => ValueTask.FromResult(new ReadResponse
            {
                Results = ids.ToArray()!.Select(id => new DataValue(
                    id.NodeId == new NodeId(BrowseNames.Identities, 2)
                        ? Variant.FromStructure(present ? (ArrayOf<IdentityMappingRuleType>)[identity] : [])
                        : id.NodeId == new NodeId(BrowseNames.Applications, 2)
                            ? Variant.From(present ? (ArrayOf<string>)s_applications : [])
                            : id.NodeId == new NodeId(BrowseNames.Endpoints, 2)
                                ? Variant.FromStructure(present ? (ArrayOf<EndpointType>)[endpoint] : [])
                                : RoleValue(id))).ToArray()
            });
            ArrayOf<CallMethodRequest> sent = default;
            context.Call = (requests, _) =>
            {
                sent = requests;
                if (!reject)
                {
                    present = !remove;
                }
                return ValueTask.FromResult(new CallResponse
                {
                    Results = [new CallMethodResult
                    {
                        StatusCode = reject ? StatusCodes.BadUserAccessDenied : StatusCodes.Good
                    }]
                });
            };
            await using var plugin = new RoleManagementPlugin(context.Host);
            var role = new RoleVm(new Opc.Ua.Client.Roles.RoleInfo(
                s_roleId, new QualifiedName("Operators"),
                remove ? [identity] : [], remove ? s_applications : [], false,
                remove ? [endpoint] : [], false, false));
            plugin.Roles.Add(role);
            plugin.SelectedRole = role;
            if (remove)
            {
                await (member switch
                {
                    "Identity" => plugin.RemoveIdentityAsync(identity),
                    "Application" => plugin.RemoveApplicationAsync(s_applications[0]),
                    _ => plugin.RemoveEndpointAsync(endpoint)
                }).ConfigureAwait(true);
            }
            else
            {
                Task operation = Task.CompletedTask;
                Window dialog = await DesktopInteraction.OpenedAsync<Window>(() => operation = member switch
                {
                    "Identity" => plugin.AddIdentityAsync(),
                    "Application" => plugin.AddApplicationAsync(),
                    _ => plugin.AddEndpointAsync()
                }).ConfigureAwait(true);
                Assert.That(sent.IsNull, Is.True);
                switch (member)
                {
                    case "Identity":
                        DesktopInteraction.Control<ComboBox>(dialog, "CriteriaTypeBox").SelectedItem =
                            IdentityCriteriaType.UserName;
                        DesktopInteraction.Control<TextBox>(dialog, "CriteriaBox").Text = "operator";
                        break;
                    case "Application":
                        DesktopInteraction.Control<TextBox>(dialog, "UriBox").Text = s_applications[0];
                        break;
                    default:
                        DesktopInteraction.Control<TextBox>(dialog, "EndpointUrlBox").Text = endpoint.EndpointUrl;
                        DesktopInteraction.Control<ComboBox>(dialog, "SecurityModeBox").SelectedItem = endpoint
                            .SecurityMode;
                        DesktopInteraction.Control<TextBox>(dialog, "SecurityPolicyBox").Text = endpoint
                            .SecurityPolicyUri;
                        DesktopInteraction.Control<TextBox>(dialog, "TransportProfileBox").Text = endpoint
                            .TransportProfileUri;
                        break;
                }
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                await operation.ConfigureAwait(true);
            }
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].ObjectId, Is.EqualTo(s_roleId));
            Assert.That(sent[0].MethodId, Is.EqualTo(new NodeId((remove ? "Remove" : "Add") + member, 2)));
            Assert.That(sent[0].InputArguments.Count, Is.EqualTo(1));
            if (member == "Identity")
            {
                Assert.That(sent[0].InputArguments[0].TryGetValue<IdentityMappingRuleType>(
                    out IdentityMappingRuleType? value, context.Messages), Is.True);
                Assert.That(value!.IsEqual(identity), Is.True);
                Assert.That(role.Identities, Has.Count.EqualTo(reject ? 0 : remove ? 0 : 1));
            }
            else if (member == "Endpoint")
            {
                Assert.That(sent[0].InputArguments[0].TryGetValue<EndpointType>(
                    out EndpointType? value, context.Messages), Is.True);
                Assert.That(value!.IsEqual(endpoint), Is.True);
                Assert.That(role.Endpoints, Has.Count.EqualTo(reject ? 0 : remove ? 0 : 1));
            }
            else
            {
                Assert.That(sent[0].InputArguments[0], Is.EqualTo(Variant.From(s_applications[0])));
                Assert.That(role.Applications, Has.Count.EqualTo(reject ? 0 : remove ? 0 : 1));
            }
            Assert.That(plugin.Status, Does.Contain(reject ? "failed:" : remove ? "Removed" : "Added"));
        });
    }

    [TestCase("Add", false)]
    [TestCase("Add", true)]
    [TestCase("Modify", false)]
    [TestCase("Modify", true)]
    [TestCase("Password", false)]
    [TestCase("Password", true)]
    public Task UserMutationsUseTheAcceptedCredentialsAndReportServerRefusals(string operation, bool reject)
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
            ArrayOf<CallMethodRequest> sent = default;
            context.Call = async (requests, _) =>
            {
                sent = requests;
                await Task.Yield();
                return new CallResponse
                {
                    Results = [new CallMethodResult
                    {
                        StatusCode = reject ? StatusCodes.BadUserAccessDenied : StatusCodes.Good
                    }]
                };
            };
            await using var plugin = new UserManagementPlugin(context.Host);
            bool onDesktop = true;
            plugin.PropertyChanged += (_, _) => onDesktop &= Dispatcher.UIThread.CheckAccess();
            var selected = new UserVm(new UserManagementUser("operator", UserConfigurationMask.None, "Current"));
            plugin.SelectedUser = selected;
            string password = Guid.NewGuid().ToString("N");
            string oldPassword = Guid.NewGuid().ToString("N");
            Task task = Task.CompletedTask;
            Window dialog = await DesktopInteraction.OpenedAsync<Window>(() => task = operation switch
            {
                "Add" => plugin.AddUserAsync(),
                "Modify" => plugin.ModifyUserAsync(selected),
                _ => plugin.ChangePasswordAsync()
            }).ConfigureAwait(true);
            Assert.That(sent.IsNull, Is.True);
            if (operation == "Add")
            {
                DesktopInteraction.Control<TextBox>(dialog, "UserNameBox").Text = "new-user";
                DesktopInteraction.Control<TextBox>(dialog, "PasswordBox").Text = password;
                DesktopInteraction.Control<TextBox>(dialog, "DescriptionBox").Text = "Description";
                DesktopInteraction.Control<CheckBox>(dialog, "NoDeleteBox").IsChecked = true;
            }
            else if (operation == "Modify")
            {
                DesktopInteraction.Control<CheckBox>(dialog, "ChangePasswordBox").IsChecked = true;
                DesktopInteraction.Control<CheckBox>(dialog, "ChangeConfigBox").IsChecked = true;
                DesktopInteraction.Control<CheckBox>(dialog, "ChangeDescriptionBox").IsChecked = true;
                DesktopInteraction.Control<TextBox>(dialog, "NewPasswordBox").Text = password;
                DesktopInteraction.Control<TextBox>(dialog, "DescriptionBox").Text = "Description";
                DesktopInteraction.Control<CheckBox>(dialog, "DisabledBox").IsChecked = true;
            }
            else
            {
                DesktopInteraction.Control<TextBox>(dialog, "OldPasswordBox").Text = oldPassword;
                DesktopInteraction.Control<TextBox>(dialog, "NewPasswordBox").Text = password;
                DesktopInteraction.Control<TextBox>(dialog, "ConfirmPasswordBox").Text = password;
            }
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
            await task.ConfigureAwait(true);
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].ObjectId, Is.EqualTo(new NodeId(Objects.UserManagement)));
            ArrayOf<Variant> args = sent[0].InputArguments;
            if (operation == "Add")
            {
                Assert.That(args.Count, Is.EqualTo(4));
                Assert.That(args[0], Is.EqualTo(Variant.From("new-user")));
                Assert.That(args[1], Is.EqualTo(Variant.From(password)));
                Assert.That(args[2], Is.EqualTo(Variant.From((uint)UserConfigurationMask.NoDelete)));
                Assert.That(args[3], Is.EqualTo(Variant.From("Description")));
            }
            else if (operation == "Modify")
            {
                Assert.That(args.Count, Is.EqualTo(7));
                Assert.That(args[0], Is.EqualTo(Variant.From("operator")));
                Assert.That(args[1], Is.EqualTo(Variant.From(true)));
                Assert.That(args[2], Is.EqualTo(Variant.From(password)));
                Assert.That(args[3], Is.EqualTo(Variant.From(true)));
                Assert.That(args[4], Is.EqualTo(Variant.From((uint)UserConfigurationMask.Disabled)));
                Assert.That(args[5], Is.EqualTo(Variant.From(true)));
                Assert.That(args[6], Is.EqualTo(Variant.From("Description")));
            }
            else
            {
                Assert.That(args.Count, Is.EqualTo(2));
                Assert.That(args[0], Is.EqualTo(Variant.From(oldPassword)));
                Assert.That(args[1], Is.EqualTo(Variant.From(password)));
            }
            Assert.That(plugin.Status, Does.Contain(reject ? "failed:" :
                operation == "Add" ? "Added user" : operation == "Modify" ? "Modified user" : "Password changed"));
            Assert.That(selected.UserName, Is.EqualTo("operator"));
            Assert.That(onDesktop, Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task AddingRolesPreservesTheChosenNamespaceAndReportsRejectedCreation(bool reject)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            ArrayOf<CallMethodRequest> sent = default;
            context.Call = (requests, _) =>
            {
                sent = requests;
                return ValueTask.FromResult(new CallResponse
                {
                    Results = [new CallMethodResult
                    {
                        StatusCode = reject ? StatusCodes.BadUserAccessDenied : StatusCodes.Good,
                        OutputArguments = reject ? [] : [Variant.From(s_roleId)]
                    }]
                });
            };
            await using var plugin = new RoleManagementPlugin(context.Host);
            Task adding = Task.CompletedTask;
            AddRoleDialog dialog = await DesktopInteraction.OpenedAsync<AddRoleDialog>(
                () => adding = plugin.AddRoleAsync()).ConfigureAwait(true);
            DesktopInteraction.Control<TextBox>(dialog, "NameBox").Text = "Operator role";
            DesktopInteraction.Control<TextBox>(dialog, "NamespaceBox").Text = "urn:roles:fixture";
            Assert.That(sent.IsNull, Is.True);
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
            await adding.ConfigureAwait(true);
            Assert.That(sent[0].ObjectId, Is.EqualTo(ObjectIds.Server_ServerCapabilities_RoleSet));
            Assert.That(sent[0].MethodId, Is.EqualTo(MethodIds.Server_ServerCapabilities_RoleSet_AddRole));
            Assert.That(sent[0].InputArguments[0], Is.EqualTo(Variant.From("Operator role")));
            Assert.That(sent[0].InputArguments[1], Is.EqualTo(Variant.From("urn:roles:fixture")));
            Assert.That(plugin.Status, Does.Contain(reject ? "Add role failed:" : "Added role Operator role"));
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
                    Targets = [new BrowsePathTarget {
                        TargetId = new NodeId(name, 2),
                        RemainingPathIndex = uint.MaxValue }]
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
