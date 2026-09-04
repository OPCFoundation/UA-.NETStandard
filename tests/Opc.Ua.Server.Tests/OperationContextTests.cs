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

using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for <see cref="OperationContext"/>, in particular which identity a
    /// context built from a monitored item carries.
    /// </summary>
    [TestFixture]
    [Category("OperationContext")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class OperationContextTests
    {
        /// <summary>
        /// The Role a Session earns from the role manager rather than from its
        /// token, and which therefore only ever appears in EffectiveIdentity.
        /// </summary>
        private static readonly NodeId s_grantedRoleId = ObjectIds.WellKnownRole_SecurityAdmin;

        /// <summary>
        /// The Role every Session holds, per Part 18 4.3.
        /// </summary>
        private static readonly NodeId s_baseRoleId = ObjectIds.WellKnownRole_Anonymous;

        /// <summary>
        /// A context built from a monitored item has to carry the Session's
        /// EffectiveIdentity. Identity is the token as it arrived; the Roles the
        /// SessionManager resolves through <see cref="IRoleManager.ResolveGrantedRoles"/>
        /// are layered onto EffectiveIdentity only, so a permission check made
        /// against Identity sees a Session that holds nothing it was granted.
        /// </summary>
        [Test]
        public void ContextFromMonitoredItemCarriesTheSessionsEffectiveIdentity()
        {
            Mock<IMonitoredItem> monitoredItem = CreateMonitoredItem(out _);

            using var context = new OperationContext(monitoredItem.Object);

            Assert.That(
                context.UserIdentity.GrantedRoleIds.ToArray(),
                Does.Contain(s_grantedRoleId),
                "A permission check from a monitored item must see the Roles the " +
                "Session was granted, not only those its token arrived with.");
        }

        /// <summary>
        /// Without a Session there is nothing to take an effective identity from,
        /// so the monitored item's own identity stands.
        /// </summary>
        [Test]
        public void ContextFromMonitoredItemWithoutASessionKeepsTheItemsIdentity()
        {
            var identity = new UserIdentity();

            var monitoredItem = new Mock<IMonitoredItem>();
            monitoredItem.SetupGet(item => item.Session).Returns((ISession)null);
            monitoredItem.SetupGet(item => item.EffectiveIdentity).Returns(identity);

            using var context = new OperationContext(monitoredItem.Object);

            Assert.That(context.UserIdentity, Is.SameAs(identity));
        }

        /// <summary>
        /// What the identity is for: Part 3 8.55 makes ReceiveEvents on an event's
        /// SourceNode a condition of delivery, and the standard node set grants it
        /// on a Role node to SecurityAdmin alone. Taken against the Session's token
        /// the check refuses a SecurityAdmin, which silently drops every
        /// RoleMappingRuleChangedAuditEventType - the one audit event whose
        /// SourceNode is a Role rather than the Server object.
        /// </summary>
        [Test]
        public void ReceiveEventsIsGrantedToARoleTheSessionEarnedFromTheRoleManager()
        {
            Mock<IMonitoredItem> monitoredItem = CreateMonitoredItem(out _);

            var metadata = new NodeMetadata(new object(), ObjectIds.WellKnownRole_Operator)
            {
                RolePermissions = ArrayOf.Wrapped(new[]
                {
                    new RolePermissionType
                    {
                        RoleId = s_baseRoleId,
                        Permissions = (uint)(PermissionType.Browse | PermissionType.Read),
                    },
                    new RolePermissionType
                    {
                        RoleId = s_grantedRoleId,
                        Permissions = (uint)PermissionType.ReceiveEvents,
                    },
                }),
            };

            using var context = new OperationContext(monitoredItem.Object);

            ServiceResult result = MasterNodeManager.ValidateRolePermissions(
                context,
                metadata,
                PermissionType.ReceiveEvents);

            Assert.That(
                ServiceResult.IsGood(result),
                Is.True,
                $"A SecurityAdmin has ReceiveEvents on this node, but the check answered {result}.");
        }

        /// <summary>
        /// A monitored item on a Session whose token grants only the Anonymous Role
        /// while the role manager added SecurityAdmin, which is what every Session
        /// that signs in against a configured role manager looks like.
        /// </summary>
        private static Mock<IMonitoredItem> CreateMonitoredItem(out ISession session)
        {
            // UserIdentity grants the Anonymous Role and nothing else, which is what a
            // token carries before the SessionManager resolves the rest.
            var token = new UserIdentity();

            var effective = new RoleBasedIdentity(
                token,
                [new Role(s_grantedRoleId, nameof(ObjectIds.WellKnownRole_SecurityAdmin))],
                new NamespaceTable());

            var sessionMock = new Mock<ISession>();
            sessionMock.SetupGet(s => s.Identity).Returns(token);
            sessionMock.SetupGet(s => s.EffectiveIdentity).Returns(effective);
            sessionMock.SetupGet(s => s.PreferredLocales).Returns([]);

            session = sessionMock.Object;

            var monitoredItem = new Mock<IMonitoredItem>();
            monitoredItem.SetupGet(item => item.Session).Returns(session);
            monitoredItem.SetupGet(item => item.EffectiveIdentity).Returns(effective);

            return monitoredItem;
        }
    }
}
