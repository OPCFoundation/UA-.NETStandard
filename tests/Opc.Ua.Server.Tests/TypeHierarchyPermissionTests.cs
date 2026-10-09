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

using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Part 3 §8.55/§8.56: type hierarchy nodes stay browsable and readable for
    /// everyone, but every other permission bit (ReceiveEvents, Call, Write,
    /// AddReference, DeleteNode, ...) is enforced on them like on any other node.
    /// </summary>
    [TestFixture]
    [Category("MasterNodeManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TypeHierarchyPermissionTests
    {
        [TestCase(PermissionType.Browse)]
        [TestCase(PermissionType.Read)]
        [TestCase(PermissionType.ReadRolePermissions)]
        [TestCase(PermissionType.Browse | PermissionType.Read)]
        public void TypeHierarchyNodeStaysVisibleWithoutGrant(PermissionType requested)
        {
            OperationContext context = CreateContext(ObjectIds.WellKnownRole_AuthenticatedUser);
            NodeMetadata metadata = CreateTypeMetadata(
                ObjectIds.WellKnownRole_SecurityAdmin,
                PermissionType.Browse | PermissionType.Read | PermissionType.Call);

            ServiceResult result = MasterNodeManager.ValidateRolePermissions(
                context,
                metadata,
                requested);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [TestCase(PermissionType.ReceiveEvents)]
        [TestCase(PermissionType.Call)]
        [TestCase(PermissionType.WriteAttribute)]
        [TestCase(PermissionType.WriteRolePermissions)]
        [TestCase(PermissionType.Write)]
        [TestCase(PermissionType.AddReference)]
        [TestCase(PermissionType.RemoveReference)]
        [TestCase(PermissionType.DeleteNode)]
        [TestCase(PermissionType.Browse | PermissionType.Write)]
        public void TypeHierarchyNodeEnforcesNonVisibilityPermissions(PermissionType requested)
        {
            OperationContext context = CreateContext(ObjectIds.WellKnownRole_Anonymous);
            NodeMetadata metadata = CreateTypeMetadata(
                ObjectIds.WellKnownRole_Anonymous,
                PermissionType.Browse | PermissionType.Read);

            ServiceResult result = MasterNodeManager.ValidateRolePermissions(
                context,
                metadata,
                requested);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void TypeHierarchyNodeGrantsReceiveEventsToGrantedRole()
        {
            OperationContext context = CreateContext(ObjectIds.WellKnownRole_SecurityAdmin);
            NodeMetadata metadata = CreateTypeMetadata(
                ObjectIds.WellKnownRole_SecurityAdmin,
                PermissionType.Browse | PermissionType.ReceiveEvents);

            ServiceResult result = MasterNodeManager.ValidateRolePermissions(
                context,
                metadata,
                PermissionType.ReceiveEvents);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void TypeHierarchyNodeWithoutRolePermissionsAllowsReceiveEvents()
        {
            OperationContext context = CreateContext(ObjectIds.WellKnownRole_Anonymous);
            var metadata = new NodeMetadata(null!, ObjectTypeIds.BaseEventType)
            {
                IsPartOfTypeHierarchy = true
            };

            ServiceResult result = MasterNodeManager.ValidateRolePermissions(
                context,
                metadata,
                PermissionType.ReceiveEvents);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void TypeHierarchyNodeAccessRestrictionsSkippedOnlyForVisibility()
        {
            OperationContext context = CreateContext(
                ObjectIds.WellKnownRole_Anonymous,
                MessageSecurityMode.None);
            var metadata = new NodeMetadata(null!, new NodeId(1000))
            {
                IsPartOfTypeHierarchy = true,
                AccessRestrictions = AccessRestrictionType.EncryptionRequired
            };

            Assert.That(
                MasterNodeManager.ValidateAccessRestrictions(context, metadata, PermissionType.Browse)
                    .StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(
                MasterNodeManager.ValidateAccessRestrictions(context, metadata, PermissionType.Read)
                    .StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(
                MasterNodeManager.ValidateAccessRestrictions(context, metadata).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(
                MasterNodeManager.ValidateAccessRestrictions(context, metadata, PermissionType.Call)
                    .StatusCode,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(
                MasterNodeManager.ValidateAccessRestrictions(context, metadata, PermissionType.Write)
                    .StatusCode,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        /// <summary>
        /// The normative ns0 NodeSet grants Anonymous only Browse and Read on
        /// AuditEventType and its subtypes (SecurityAdmin gets all permissions).
        /// An Anonymous subscriber must therefore not receive audit events while
        /// it still receives events whose type carries no RolePermissions.
        /// </summary>
        [Test]
        public async Task AuditEventTypeReceiveEventsIsEnforcedForAnonymousAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                var nodeManager = server.CurrentInstance.DiagnosticsNodeManager as AsyncCustomNodeManager;
                Assert.That(nodeManager, Is.Not.Null);

                OperationContext anonymous = CreateContext(ObjectIds.WellKnownRole_Anonymous);
                OperationContext securityAdmin = CreateContext(ObjectIds.WellKnownRole_SecurityAdmin);

                ServiceResult audit = await nodeManager.ValidateEventReceivePermissionsAsync(
                    anonymous,
                    ObjectTypeIds.AuditCreateSessionEventType,
                    ObjectIds.Server).ConfigureAwait(false);
                Assert.That(audit.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

                ServiceResult auditBase = await nodeManager.ValidateEventReceivePermissionsAsync(
                    anonymous,
                    ObjectTypeIds.AuditEventType,
                    ObjectIds.Server).ConfigureAwait(false);
                Assert.That(auditBase.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

                ServiceResult plain = await nodeManager.ValidateEventReceivePermissionsAsync(
                    anonymous,
                    ObjectTypeIds.BaseEventType,
                    ObjectIds.Server).ConfigureAwait(false);
                Assert.That(plain.StatusCode, Is.EqualTo(StatusCodes.Good));

                ServiceResult alarm = await nodeManager.ValidateEventReceivePermissionsAsync(
                    anonymous,
                    ObjectTypeIds.AlarmConditionType,
                    ObjectIds.Server).ConfigureAwait(false);
                Assert.That(alarm.StatusCode, Is.EqualTo(StatusCodes.Good));

                ServiceResult admin = await nodeManager.ValidateEventReceivePermissionsAsync(
                    securityAdmin,
                    ObjectTypeIds.AuditCreateSessionEventType,
                    ObjectIds.Server).ConfigureAwait(false);
                Assert.That(admin.StatusCode, Is.EqualTo(StatusCodes.Good));

                // Visibility of the audit type itself is unchanged for Anonymous.
                ServiceResult browse = await nodeManager.ValidateRolePermissionsAsync(
                    anonymous,
                    ObjectTypeIds.AuditEventType,
                    PermissionType.Browse).ConfigureAwait(false);
                Assert.That(browse.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Part 3 §8.55 Call: the Call service needs the Call bit on the Object
        /// passed as ObjectId as well as on the Method. Removing Call from the
        /// Server object for Anonymous must deny Server.GetMonitoredItems even
        /// though the Method itself still grants Call to Anonymous.
        /// </summary>
        [Test]
        public async Task CallServiceChecksCallPermissionOfObjectAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                var nodeManager = server.CurrentInstance.DiagnosticsNodeManager as AsyncCustomNodeManager;
                Assert.That(nodeManager, Is.Not.Null);
                BaseObjectState serverObject = nodeManager.FindPredefinedNode<BaseObjectState>(ObjectIds.Server);
                Assert.That(serverObject, Is.Not.Null);

                OperationContext anonymous = CreateContext(
                    ObjectIds.WellKnownRole_Anonymous,
                    requestType: RequestType.Call);
                ArrayOf<CallMethodRequest> requests =
                [
                    new CallMethodRequest
                    {
                        ObjectId = ObjectIds.Server,
                        MethodId = MethodIds.Server_GetMonitoredItems,
                        InputArguments = [new Variant(4242u)]
                    }
                ];

                // Object and Method grant Call: the method runs and rejects the unknown subscription.
                (ArrayOf<CallMethodResult> allowed, _) = await server.CurrentInstance.NodeManager
                    .CallAsync(anonymous, requests).ConfigureAwait(false);
                Assert.That(allowed[0].StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));

                ArrayOf<RolePermissionType> original = serverObject.RolePermissions;
                try
                {
                    serverObject.RolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_Anonymous,
                            Permissions = (uint)(PermissionType.Browse | PermissionType.ReceiveEvents)
                        }
                    ];

                    (ArrayOf<CallMethodResult> denied, _) = await server.CurrentInstance.NodeManager
                        .CallAsync(anonymous, requests).ConfigureAwait(false);
                    Assert.That(denied[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                }
                finally
                {
                    serverObject.RolePermissions = original;
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static NodeMetadata CreateTypeMetadata(NodeId roleId, PermissionType permissions)
        {
            return new NodeMetadata(null!, new NodeId(1000))
            {
                IsPartOfTypeHierarchy = true,
                RolePermissions =
                [
                    new RolePermissionType { RoleId = roleId, Permissions = (uint)permissions }
                ]
            };
        }

        private static OperationContext CreateContext(
            NodeId roleId,
            MessageSecurityMode securityMode = MessageSecurityMode.SignAndEncrypt,
            RequestType requestType = RequestType.Read)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(x => x.GrantedRoleIds).Returns([roleId]);
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            var channelContext = new SecureChannelContext("test", endpoint, RequestEncoding.Binary);
            return new OperationContext(
                new RequestHeader(),
                channelContext,
                requestType,
                RequestLifetime.None,
                identity.Object);
        }
    }
}
