// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Roles")]
    [Parallelizable]
    public class GroupIdCriteriaTests
    {
        private static readonly string[] s_engineeringLeadGroups = ["engineering-leads"];
        private static readonly string[] s_operatorGroups = ["operators"];

        [Test]
        public void ResolveGrantedRoles_GroupClaimMatchingCriteria_GrantsRole()
        {
            AssertMessageContextCanBeCreated();
            using RoleManager manager = CreateManagerWithGroupRule("engineering-leads");
            var identity = new ClaimsTestIdentity(groups: s_engineeringLeadGroups);

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);

            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Engineer));
        }

        [Test]
        public void ResolveGrantedRoles_GroupClaimNotMatchingCriteria_DoesNotGrantRole()
        {
            AssertMessageContextCanBeCreated();
            using RoleManager manager = CreateManagerWithGroupRule("engineering-leads");
            var identity = new ClaimsTestIdentity(groups: s_operatorGroups);

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);

            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Engineer));
        }

        [Test]
        public void ResolveGrantedRoles_IdentityWithoutClaims_DoesNotGrantGroupRole()
        {
            AssertMessageContextCanBeCreated();
            using RoleManager manager = CreateManagerWithGroupRule("engineering-leads");
            IUserIdentity identity = CreatePlainIdentity();

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);

            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Engineer));
        }

        [Test]
        public void ResolveGrantedRoles_EmptyGroups_DoesNotGrantGroupRole()
        {
            AssertMessageContextCanBeCreated();
            using RoleManager manager = CreateManagerWithGroupRule("engineering-leads");
            var identity = new ClaimsTestIdentity(groups: []);

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);

            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Engineer));
        }

        [Test]
        public void ResolveGrantedRoles_GroupCriteriaBehindRoleWrapper_GrantsRole()
        {
            AssertMessageContextCanBeCreated();
            using RoleManager manager = CreateManagerWithGroupRule("engineering-leads");
            IUserIdentity identity = new RoleBasedIdentity(
                new ClaimsTestIdentity(groups: s_engineeringLeadGroups),
                [Role.TrustedApplication],
                new NamespaceTable());

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);

            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Engineer));
        }

        private static RoleManager CreateManagerWithGroupRule(string groupId)
        {
            var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(
                ObjectIds.WellKnownRole_Engineer,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.GroupId,
                    Criteria = groupId
                });
            Assert.That(ServiceResult.IsGood(result), Is.True);
            return manager;
        }

        private static IUserIdentity CreatePlainIdentity()
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.IssuedToken);
            identity.Setup(i => i.DisplayName).Returns("claims-user");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);
            return identity.Object;
        }

        private static void AssertMessageContextCanBeCreated()
        {
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            Assert.That(context, Is.Not.Null);
        }
    }
}
