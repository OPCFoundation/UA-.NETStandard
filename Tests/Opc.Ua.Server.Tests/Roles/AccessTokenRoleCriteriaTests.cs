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
            using var manager = CreateManagerWithRoleRule("Engineer");
            var identity = new ClaimsTestIdentity(roles: s_engineerRoles);

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);

            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Engineer));
        }

        [Test]
        public void ResolveGrantedRoles_PrefixedAccessTokenRoleMatchingIssuer_GrantsRole()
        {
            AssertMessageContextCanBeCreated();
            using var manager = CreateManagerWithRoleRule("https://idp.example.com/Engineer");
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
            using var manager = CreateManagerWithRoleRule("https://idp.example.com/Engineer");
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

            using var manager = CreateManagerWithRoleRule(grantedRoleCriteria);
            var identity = new ClaimsTestIdentity(
                tokenType: UserTokenType.UserName,
                roles: s_engineerRoles);
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);
            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Engineer),
                "Role criteria must read access-token role claims, not already-granted role NodeIds.");
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
