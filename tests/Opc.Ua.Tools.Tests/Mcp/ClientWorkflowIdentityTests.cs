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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion.ClientWorkflows
{
    /// <summary>
    /// Proves exact identity-administration wire contracts, host/server denials and secret confidentiality.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class ClientWorkflowIdentityTests
    {
        /// <summary>
        /// Every fixed mapping tool resolves the qualified method and passes the exact typed argument.
        /// </summary>
        [TestCase("roles_add_identity", BrowseNames.AddIdentity, "identity")]
        [TestCase("roles_remove_identity", BrowseNames.RemoveIdentity, "identity")]
        [TestCase("roles_add_application", BrowseNames.AddApplication, "application")]
        [TestCase("roles_remove_application", BrowseNames.RemoveApplication, "application")]
        [TestCase("roles_add_endpoint", BrowseNames.AddEndpoint, "endpoint")]
        [TestCase("roles_remove_endpoint", BrowseNames.RemoveEndpoint, "endpoint")]
        public async Task MappingMutationsPreserveExactTypedTargetArgumentsAndNonDefaultStatusAsync(
            string tool, string method, string kind)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId role = fixture.Role();
            fixture.Remote.Reply(StatusCodes.GoodCompletesAsynchronously);
            using IDisposable caller = fixture.Enter();
            JsonObject arguments = MappingArguments(role, kind);
            CallToolResult result = await fixture.InvokeAsync(tool, arguments).ConfigureAwait(false);

            AssertStatus(result, StatusCodes.GoodCompletesAsynchronously);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            CallMethodRequest request = fixture.Remote.Calls[0];
            Assert.That(request.ObjectId, Is.EqualTo(role));
            Assert.That(request.MethodId, Is.EqualTo(fixture.Remote.Id(method)));
            Assert.That(request.InputArguments.Count, Is.EqualTo(1));
            Variant argument = request.InputArguments[0];
            if (kind == "identity")
            {
                Assert.That(argument.TryGetStructure<IdentityMappingRuleType>(
                    out IdentityMappingRuleType? identity), Is.True);
                Assert.That(identity!.CriteriaType, Is.EqualTo(IdentityCriteriaType.UserName));
                Assert.That(identity.Criteria, Is.EqualTo("alice"));
            }
            else if (kind == "application")
            {
                Assert.That(argument, Is.EqualTo(Variant.From("urn:plant:client")));
            }
            else
            {
                Assert.That(argument.TryGetStructure<EndpointType>(out EndpointType? endpoint), Is.True);
                Assert.That(endpoint!.EndpointUrl, Is.EqualTo("opc.tcp://server.invalid:4840"));
                Assert.That(endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(endpoint.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            }
            Assert.That(fixture.Remote.Paths[^1].RelativePath.Elements[0].TargetName,
                Is.EqualTo(new QualifiedName(method)));
        }

        /// <summary>
        /// Create and remove are single standard RoleSet calls with no hidden mapping side effects.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RoleCreationAndRemovalPreserveExactStandardRoleSetCallAsync(bool remove)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId role = fixture.Role();
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(role));
            using IDisposable caller = fixture.Enter();
            CallToolResult result = remove
                ? await fixture.Roles.RemoveAsync(role.ToString(), "selected").ConfigureAwait(false)
                : await fixture.Roles.CreateAsync("Maintenance", "urn:plant:roles", "selected").ConfigureAwait(false);
            AssertStatus(result, StatusCodes.Good);
            fixture.Remote.AssertCall(ObjectIds.Server_ServerCapabilities_RoleSet,
                remove ? MethodIds.Server_ServerCapabilities_RoleSet_RemoveRole :
                    MethodIds.Server_ServerCapabilities_RoleSet_AddRole,
                remove ? [Variant.From(role)] : [Variant.From("Maintenance"), Variant.From("urn:plant:roles")]);
            if (!remove)
            {
                Assert.That(RemoteModelTestSession.Payload(result).GetProperty("createdRole")
                    .GetProperty("nodeId").GetString(), Is.EqualTo(role.ToString()));
            }
        }

        /// <summary>
        /// Reserved-role/self-lockout denials are returned as the server sent them, never worked around.
        /// </summary>
        [Test]
        public async Task ReservedRoleServerRefusalIsPreservedWithoutFallbackOrRetryAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId role = fixture.Role();
            fixture.Remote.Reply(StatusCodes.BadUserAccessDenied);
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.Roles.RemoveAsync(role.ToString(), "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            Assert.That(fixture.Writes, Is.Empty);
        }

        /// <summary>
        /// Boolean false is an explicit flag update while omission is not a write.
        /// </summary>
        [Test]
        public async Task RoleFlagsPreserveFalseOmissionOrderingAndServerWriteStatusAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId role = fixture.Role();
            NodeId exclude = fixture.Remote.Property(role, new QualifiedName(BrowseNames.ApplicationsExclude),
                Variant.From(true));
            NodeId custom = fixture.Remote.Property(role, new QualifiedName(BrowseNames.CustomConfiguration),
                Variant.From(false));
            fixture.WriteStatus = StatusCodes.GoodClamped;
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.Roles.SetFlagsAsync(role.ToString(),
                applicationsExclude: false, customConfiguration: true, sessionName: "selected").ConfigureAwait(false);

            Assert.That(fixture.Writes, Has.Count.EqualTo(2));
            Assert.That(fixture.Writes.Select(write => write.NodeId), Is.EqualTo([exclude, custom]));
            Assert.That(fixture.Writes.Select(write => write.AttributeId), Is.All.EqualTo(Attributes.Value));
            Assert.That(fixture.Writes[0].Value.WrappedValue, Is.EqualTo(Variant.From(false)));
            Assert.That(fixture.Writes[1].Value.WrappedValue, Is.EqualTo(Variant.From(true)));
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("results")
                .EnumerateArray().Select(item => item.GetProperty("statusCodeValue").GetUInt32()),
                Is.All.EqualTo(StatusCodes.GoodClamped.Code));
        }

        /// <summary>
        /// Declared permission replacement uses the exact attribute and typed role array, not effective permissions.
        /// </summary>
        [Test]
        public async Task DeclaredPermissionUpdatePreservesRoleMaskAndServerDenialAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId target = fixture.Remote.Id("variable");
            NodeId role = fixture.Role();
            fixture.WriteStatus = StatusCodes.BadNotWritable;
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.Roles.WritePermissionsAsync(target.ToString(),
                [new WorkflowRolePermissionInput { RoleId = role.ToString(), Permissions = 33 }],
                "selected").ConfigureAwait(false);
            Assert.That(result.IsError, Is.True);
            Assert.That(fixture.Writes, Has.Count.EqualTo(1));
            Assert.That(fixture.Writes[0].NodeId, Is.EqualTo(target));
            Assert.That(fixture.Writes[0].AttributeId, Is.EqualTo(Attributes.RolePermissions));
            Assert.That(fixture.Writes[0].Value.WrappedValue.TryGetStructure(
                out ArrayOf<RolePermissionType> permissions), Is.True);
            Assert.That(permissions.Count, Is.EqualTo(1));
            Assert.That(permissions[0].RoleId, Is.EqualTo(role));
            Assert.That(permissions[0].Permissions, Is.EqualTo(33));
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("results")[0]
                .GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(StatusCodes.BadNotWritable.Code));
        }

        /// <summary>
        /// Every exposed administrative mutation remains gated on direct invocation.
        /// </summary>
        [TestCaseSource(nameof(IdentityMutations))]
        public async Task EveryIdentityMutationRefusesHostDisabledBeforeRemoteEffectsAsync(
            string tool, JsonObject arguments)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync(enabled: false).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.InvokeAsync(tool, (JsonObject)arguments.DeepClone())
                .ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Writes, Is.Empty);
            Assert.That(fixture.Secrets, Is.Empty);
        }

        /// <summary>
        /// Exact caller/session scope is distinct from both host enablement and another caller's permission.
        /// </summary>
        [Test]
        public async Task WrongCallerSessionScopeRefusesAdministrationBeforeSecretResolutionAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter(ClientWorkflowTestContext.WrongSession);
            CallToolResult result = await fixture.Users.AddAsync(
                "alice", "alias:new", sessionName: "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Secrets, Is.Empty);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// Host permission never relaxes the encrypted UA channel requirement.
        /// </summary>
        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        public async Task AdministrationRefusesNonEncryptedSessionsAsync(MessageSecurityMode mode)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync(mode: mode).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            RemoteModelTestSession.Error(await fixture.Users.RemoveAsync("alice", "selected").ConfigureAwait(false),
                StatusCodes.BadSecurityModeInsufficient);
            RemoteModelTestSession.Error(await fixture.Roles.CreateAsync("role", "urn:roles", "selected")
                .ConfigureAwait(false), StatusCodes.BadSecurityModeInsufficient);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// Add resolves an authorized secret but no output argument may reflect it.
        /// </summary>
        [Test]
        public async Task AddUserBindsSecretReferenceAndNeverReturnsUnexpectedSecretOutputAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.Remote.Reply(StatusCodes.GoodClamped, Variant.From("new-test-only"));
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.InvokeAsync("users_add", new JsonObject
            {
                ["userName"] = "alice",
                ["passwordReference"] = "alias:new",
                ["userConfiguration"] = 2,
                ["description"] = "Operator"
            }).ConfigureAwait(false);
            AssertStatus(result, StatusCodes.GoodClamped);
            fixture.Remote.AssertCall(ObjectIds.UserManagement, MethodIds.UserManagementType_AddUser,
                Variant.From("alice"), Variant.From("new-test-only"), Variant.From(2u), Variant.From("Operator"));
            Assert.That(fixture.Secrets, Has.Count.EqualTo(1));
            Assert.That(fixture.Secrets[0].Disposed, Is.True);
            Assert.That(RemoteModelTestSession.Payload(result).GetRawText(), Does.Not.Contain("new-test-only"));
            Assert.That(RemoteModelTestSession.Payload(result).TryGetProperty("outputArguments", out _), Is.False);
        }

        /// <summary>
        /// Null means unchanged, while false/zero/empty are concrete ModifyUser values.
        /// </summary>
        [Test]
        public async Task ModifyUserDistinguishesUnchangedPasswordFromExplicitClearedDescriptionAndZeroFlagsAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.Remote.Reply(StatusCodes.Good);
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.Users.ModifyAsync("alice",
                userConfiguration: 0, description: string.Empty, sessionName: "selected").ConfigureAwait(false);
            AssertStatus(result, StatusCodes.Good);
            fixture.Remote.AssertCall(ObjectIds.UserManagement, MethodIds.UserManagementType_ModifyUser,
                Variant.From("alice"), Variant.From(false), Variant.From(string.Empty),
                Variant.From(true), Variant.From(0u), Variant.From(true), Variant.From(string.Empty));
            Assert.That(fixture.Secrets, Is.Empty);
        }

        /// <summary>
        /// ModifyUser's password flag is explicit and omission of other fields remains unchanged.
        /// </summary>
        [Test]
        public async Task ModifyUserPasswordReferenceDoesNotModifyOmittedFieldsAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.Remote.Reply(StatusCodes.Good);
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.Users.ModifyAsync(
                "alice", "alias:new", sessionName: "selected").ConfigureAwait(false);
            AssertStatus(result, StatusCodes.Good);
            fixture.Remote.AssertCall(ObjectIds.UserManagement, MethodIds.UserManagementType_ModifyUser,
                Variant.From("alice"), Variant.From(true), Variant.From("new-test-only"),
                Variant.From(false), Variant.From(0u), Variant.From(false), Variant.From(string.Empty));
        }

        /// <summary>
        /// The removal is one exact server-authorized operation.
        /// </summary>
        [Test]
        public async Task RemoveUserPreservesExactTargetAndServerSelfLockoutRefusalAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            fixture.Remote.Reply(StatusCodes.BadUserAccessDenied);
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.Users.RemoveAsync("alice", "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            fixture.Remote.AssertCall(ObjectIds.UserManagement, MethodIds.UserManagementType_RemoveUser,
                Variant.From("alice"));
        }

        /// <summary>
        /// Self-service password change never accepts a user-name override and preserves post-commit success.
        /// </summary>
        [Test]
        public async Task SelfPasswordChangePreservesCommitAfterLateCancellationAndUsesOnlyTwoSecretArgumentsAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            fixture.Remote.OnCall = _ =>
            {
                cancellation.Cancel();
                return new CallMethodResult { StatusCode = StatusCodes.GoodCompletesAsynchronously };
            };
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.Users.ChangePasswordAsync(
                "alias:old", "alias:new", "selected", cancellation.Token).ConfigureAwait(false);
            AssertStatus(result, StatusCodes.GoodCompletesAsynchronously);
            fixture.Remote.AssertCall(ObjectIds.UserManagement, MethodIds.UserManagementType_ChangePassword,
                Variant.From("old-test-only"), Variant.From("new-test-only"));
            Assert.That(fixture.Secrets.Select(secret => secret.Disposed), Is.All.True);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("sessionMayClose").GetBoolean(), Is.True);
        }

        /// <summary>
        /// Exact resource binding rejects otherwise-granted aliases.
        /// </summary>
        [Test]
        public async Task ForeignSecretResourceIsRefusedBeforeServerCallAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.Users.AddAsync(
                "alice", "alias:foreign", sessionName: "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Secrets, Is.Empty);
        }

        /// <summary>
        /// A malicious diagnostic cannot turn secret-bearing administration into an exfiltration channel.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SecretProviderAndServerFailureMessagesAreNeverReturnedAsync(bool server)
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            const string sensitive = "not-a-real-password-should-never-appear";
            if (server)
            {
                fixture.Remote.OnCall = _ => throw new ServiceResultException(StatusCodes.BadInternalError, sensitive);
            }
            else
            {
                fixture.SecretFailure = new InvalidOperationException(sensitive);
            }
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.Users.AddAsync(
                "alice", "alias:new", sessionName: "selected").ConfigureAwait(false);
            Assert.That(result.IsError, Is.True);
            Assert.That(RemoteModelTestSession.Payload(result).GetRawText(), Does.Not.Contain(sensitive));
        }

        /// <summary>
        /// A vendor namespace lookalike is not the standard role method.
        /// </summary>
        [Test]
        public async Task RoleMappingRejectsSameNameInDifferentNamespaceAsync()
        {
            await using ClientWorkflowTestContext fixture =
                await ClientWorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId role = fixture.Remote.Id("role");
            fixture.Remote.Type(role, ObjectTypeIds.RoleType);
            fixture.Remote.Child(role, new QualifiedName(BrowseNames.AddApplication, fixture.Remote.InstanceNamespace),
                fixture.Remote.Id("lookalike"));
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.Roles.AddApplicationAsync(
                role.ToString(), "urn:client", "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadNotSupported);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// Builds the exact published mapping arguments.
        /// </summary>
        private static JsonObject MappingArguments(NodeId role, string kind)
        {
            var arguments = new JsonObject { ["roleId"] = role.ToString() };
            if (kind == "identity")
            {
                arguments["criteriaType"] = "UserName";
                arguments["criteria"] = "alice";
            }
            else if (kind == "application")
            {
                arguments["applicationUri"] = "urn:plant:client";
            }
            else
            {
                arguments["endpointUrl"] = "opc.tcp://server.invalid:4840";
                arguments["securityMode"] = "SignAndEncrypt";
                arguments["securityPolicyUri"] = SecurityPolicies.Basic256Sha256;
            }
            return arguments;
        }

        /// <summary>
        /// Enumerates all gated tools with individually valid public arguments.
        /// </summary>
        private static IEnumerable<TestCaseData> IdentityMutations()
        {
            yield return new TestCaseData("users_policy", new JsonObject());
            yield return new TestCaseData("users_list", new JsonObject());
            yield return new TestCaseData("roles_create",
                new JsonObject { ["roleName"] = "r", ["namespaceUri"] = "urn:roles" });
            yield return new TestCaseData("roles_remove", new JsonObject { ["roleId"] = "ns=1;s=role" });
            foreach (string name in new[] { "roles_add_identity", "roles_remove_identity" })
            {
                yield return new TestCaseData(name, MappingArguments(new NodeId("role", 1), "identity"));
            }
            foreach (string name in new[] { "roles_add_application", "roles_remove_application" })
            {
                yield return new TestCaseData(name, MappingArguments(new NodeId("role", 1), "application"));
            }
            foreach (string name in new[] { "roles_add_endpoint", "roles_remove_endpoint" })
            {
                yield return new TestCaseData(name, MappingArguments(new NodeId("role", 1), "endpoint"));
            }
            yield return new TestCaseData("roles_set_flags",
                new JsonObject { ["roleId"] = "ns=1;s=role", ["applicationsExclude"] = false });
            yield return new TestCaseData("roles_write_permissions",
                new JsonObject { ["nodeId"] = "ns=1;s=role", ["permissions"] = new JsonArray() });
            yield return new TestCaseData("users_add",
                new JsonObject { ["userName"] = "alice", ["passwordReference"] = "alias:new" });
            yield return new TestCaseData("users_modify",
                new JsonObject { ["userName"] = "alice", ["description"] = string.Empty });
            yield return new TestCaseData("users_remove", new JsonObject { ["userName"] = "alice" });
            yield return new TestCaseData("users_change_password",
                new JsonObject { ["oldPasswordReference"] = "alias:old", ["newPasswordReference"] = "alias:new" });
        }

        /// <summary>
        /// Asserts the exact raw status alongside the tool-level failure flag.
        /// </summary>
        private static void AssertStatus(CallToolResult result, StatusCode code)
        {
            Assert.That(result.IsError, Is.EqualTo(StatusCode.IsBad(code)));
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(code.Code));
        }
    }
}
#endif
