// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Client;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for connections with no security.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class SecurityAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task ConnectNoSecurityAsync()
        {
            // Create a session that explicitly uses SecurityPolicy None
            ISession session = await fixture.CreateSessionAsync("NoSecurity")
                .ConfigureAwait(false);

            await Assert.That(session.Connected).IsTrue();

            // Verify the endpoint uses no security
            await Assert.That(
                session.ConfiguredEndpoint.Description.SecurityPolicyUri)
                .IsEqualTo(SecurityPolicies.None);

            // Read a value to confirm the session is functional
            DataValue serverState = await session.ReadValueAsync(
                VariableIds.Server_ServerStatus_State,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(serverState.StatusCode))
                .IsTrue();

            await session.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            session.Dispose();
        }

        [Test]
        public async Task AnonymousIdentityAsync()
        {
            ISession session = await fixture.CreateSessionAsync("AnonIdentity")
                .ConfigureAwait(false);

            await Assert.That(session.Connected).IsTrue();

            // Verify the session identity is anonymous
            await Assert.That(session.Identity).IsNotNull();
            await Assert.That(session.Identity.TokenType)
                .IsEqualTo(UserTokenType.Anonymous);

            // Read ServerStatus to confirm the session works
            DataValue serverStatus = await session.ReadValueAsync(
                VariableIds.Server_ServerStatus,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(serverStatus.IsNull).IsFalse();
            await Assert.That(StatusCode.IsGood(serverStatus.StatusCode))
                .IsTrue();

            await session.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            session.Dispose();
        }

        [Test]
        public async Task NodeStateSecurityAttributesPreserveDefaultsAndResetsAsync()
        {
            var context = new SystemContext(fixture.Telemetry);
            var node = new BaseObjectState(null)
            {
                RolePermissions = default,
                UserRolePermissions = default,
                AccessRestrictions = null
            };

            await Assert.That(node.RolePermissions.IsNull).IsTrue();
            await Assert.That(node.UserRolePermissions.IsNull).IsTrue();
            await Assert.That(node.AccessRestrictions).IsNull();
            await Assert.That(node.ChangeMasks).IsEqualTo(NodeStateChangeMasks.None);

            node.RolePermissions = [];
            node.UserRolePermissions = [];
            node.AccessRestrictions = AccessRestrictionType.None;

            await Assert.That(node.RolePermissions.IsNull).IsFalse();
            await Assert.That(node.RolePermissions.Count).IsEqualTo(0);
            await Assert.That(node.UserRolePermissions.IsNull).IsFalse();
            await Assert.That(node.UserRolePermissions.Count).IsEqualTo(0);
            await Assert.That(node.AccessRestrictions.HasValue).IsTrue();
            await Assert.That(node.AccessRestrictions.GetValueOrDefault()).IsEqualTo(AccessRestrictionType.None);

            ArrayOf<RolePermissionType> permissions =
            [
                new RolePermissionType { RoleId = new NodeId(1), Permissions = (uint)PermissionType.Read }
            ];
            ArrayOf<RolePermissionType> userPermissions =
            [
                new RolePermissionType { RoleId = new NodeId(2), Permissions = (uint)PermissionType.Browse }
            ];
            node.RolePermissions = permissions;
            node.UserRolePermissions = userPermissions;
            node.AccessRestrictions = AccessRestrictionType.SigningRequired;

            await Assert.That(node.RolePermissions == permissions).IsTrue();
            await Assert.That(node.UserRolePermissions == userPermissions).IsTrue();
            await Assert.That(node.AccessRestrictions.GetValueOrDefault())
                .IsEqualTo(AccessRestrictionType.SigningRequired);
            await Assert.That(node.ChangeMasks)
                .IsEqualTo(NodeStateChangeMasks.NonValue | NodeStateChangeMasks.RolePermissions);

            var copy = (NodeState)node.Clone();
            await Assert.That(copy.RolePermissions == permissions).IsTrue();
            await Assert.That(copy.UserRolePermissions == userPermissions).IsTrue();
            // Clone is a full copy, so it carries the node's access control too.
            // Dropping AccessRestrictions silently handed out a copy that was
            // less restricted than the node it was made from.
            await Assert.That(copy.AccessRestrictions.HasValue).IsTrue();
            await Assert.That(copy.AccessRestrictions.GetValueOrDefault())
                .IsEqualTo(AccessRestrictionType.SigningRequired);

            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            node.AccessRestrictions = null;
            await Assert.That(node.AccessRestrictions).IsNull();
            await Assert.That(node.ChangeMasks).IsEqualTo(NodeStateChangeMasks.NonValue);

            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            node.RolePermissions = default;
            node.UserRolePermissions = default;
            await Assert.That(node.RolePermissions.IsNull).IsTrue();
            await Assert.That(node.UserRolePermissions.IsNull).IsTrue();
            await Assert.That(node.ChangeMasks)
                .IsEqualTo(NodeStateChangeMasks.NonValue | NodeStateChangeMasks.RolePermissions);
            await Assert.That(copy.RolePermissions == permissions).IsTrue();
            await Assert.That(copy.UserRolePermissions == userPermissions).IsTrue();

            await node.ClearChangeMasksAsync(context, false).ConfigureAwait(false);
            node.RolePermissions = default;
            node.UserRolePermissions = default;
            node.AccessRestrictions = null;
            await Assert.That(node.ChangeMasks).IsEqualTo(NodeStateChangeMasks.None);
        }
    }
}
