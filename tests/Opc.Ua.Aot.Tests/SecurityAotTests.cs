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
