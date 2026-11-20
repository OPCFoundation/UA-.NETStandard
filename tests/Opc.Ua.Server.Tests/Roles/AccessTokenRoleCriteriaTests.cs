/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Roles")]
    [Parallelizable]
    public class AccessTokenRoleCriteriaTests
    {
        private static readonly string[] s_engineerRoles = ["Engineer"];

        [Test]
        public void ResolveGrantedRoles_BareAccessTokenRoleMatchingCriteria_GrantsRole()
        {
            AssertMessageContextCanBeCreated();
            using RoleManager manager = CreateManagerWithRoleRule("Engineer");
            var identity = new ClaimsTestIdentity(roles: s_engineerRoles);

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);

            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Engineer));
        }

        [Test]
        public void ResolveGrantedRoles_PrefixedAccessTokenRoleMatchingIssuer_GrantsRole()
        {
            AssertMessageContextCanBeCreated();
            using RoleManager manager = CreateManagerWithRoleRule("https://idp.example.com/Engineer");
            var identity = new ClaimsTestIdentity(
                roles: s_engineerRoles,
                issuer: "https://idp.example.com");

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);

            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Engineer));
        }

        [Test]
        public void ResolveGrantedRoles_PrefixedAccessTokenRoleWithDifferentIssuer_DoesNotGrantRole()
        {
            AssertMessageContextCanBeCreated();
            using RoleManager manager = CreateManagerWithRoleRule("https://idp.example.com/Engineer");
            var identity = new ClaimsTestIdentity(
                roles: s_engineerRoles,
                issuer: "https://other-idp.example.com");

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);

            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Engineer));
        }

        [Test]
        public void ResolveGrantedRoles_RoleCriteriaMatchesAccessTokenRolesOnly()
        {
            AssertMessageContextCanBeCreated();
            string grantedRoleCriteria = ObjectIds.WellKnownRole_AuthenticatedUser.ToString();

            using RoleManager manager = CreateManagerWithRoleRule(grantedRoleCriteria);
            var identity = new ClaimsTestIdentity(
                tokenType: UserTokenType.UserName,
                roles: s_engineerRoles);
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);
            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Engineer),
                "Role criteria must read access-token role claims, not already-granted role NodeIds.");
        }

        [Test]
        public void ResolveGrantedRoles_RoleCriteriaBehindRoleWrapper_GrantsRole()
        {
            AssertMessageContextCanBeCreated();
            using RoleManager manager = CreateManagerWithRoleRule("Engineer");
            IUserIdentity identity = new RoleBasedIdentity(
                new ClaimsTestIdentity(roles: s_engineerRoles),
                [Role.TrustedApplication],
                new NamespaceTable());

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);

            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Engineer));
        }

        private static RoleManager CreateManagerWithRoleRule(string criteria)
        {
            var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(
                ObjectIds.WellKnownRole_Engineer,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.Role,
                    Criteria = criteria
                });
            Assert.That(ServiceResult.IsGood(result), Is.True);
            return manager;
        }

        private static void AssertMessageContextCanBeCreated()
        {
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            Assert.That(context, Is.Not.Null);
        }
    }
}
