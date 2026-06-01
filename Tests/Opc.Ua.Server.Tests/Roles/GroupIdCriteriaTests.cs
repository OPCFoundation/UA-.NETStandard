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

using System;
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
            var identity = new ClaimsTestIdentity(groups: Array.Empty<string>());

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);

            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Engineer));
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
