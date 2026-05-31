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
using System.Linq;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("RoleBasedIdentity")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RoleBasedIdentityTests
    {
        [Test]
        public void RoleEqualsReturnsTrueForSameRole()
        {
            var role = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);
            var same = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);

            bool equalsResult = role.Equals(same);
            Assert.That(equalsResult, Is.True);
        }

        [Test]
        public void RoleEqualsReturnsFalseForDifferentRole()
        {
            var role1 = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);
            var role2 = new Role(ObjectIds.WellKnownRole_Operator, BrowseNames.WellKnownRole_Operator);

            bool equalsResult = role1.Equals(role2);
            Assert.That(equalsResult, Is.False);
        }

        [Test]
        public void RoleEqualsReturnsFalseForNull()
        {
            var role = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);

            bool equalsResult = role.Equals((Role)null);
            Assert.That(equalsResult, Is.False);
        }

        [Test]
        public void RoleEqualsReturnsTrueForSameReference()
        {
            var role = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);

            bool equalsResult = role.Equals(role);
            Assert.That(equalsResult, Is.True);
        }

        [Test]
        public void RoleObjectEqualsReturnsTrueForEqualRole()
        {
            var role = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);
            object same = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);

            bool equalsResult = role.Equals(same);
            Assert.That(equalsResult, Is.True);
        }

        [Test]
        public void RoleGetHashCodeReturnsSameForEqualRoles()
        {
            var role1 = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);
            var role2 = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);

            Assert.That(role1.GetHashCode(), Is.EqualTo(role2.GetHashCode()));
        }

        [Test]
        public void RoleEqualityOperatorReturnsTrueForEqualRoles()
        {
            var role1 = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);
            var role2 = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);

            bool result = role1 == role2;
            Assert.That(result, Is.True);
        }

        [Test]
        public void RoleEqualityOperatorReturnsFalseForDifferentRoles()
        {
            var role1 = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);
            var role2 = new Role(ObjectIds.WellKnownRole_Operator, BrowseNames.WellKnownRole_Operator);

            bool result = role1 == role2;
            Assert.That(result, Is.False);
        }

        [Test]
        public void RoleInequalityOperatorReturnsTrueForDifferentRoles()
        {
            var role1 = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);
            var role2 = new Role(ObjectIds.WellKnownRole_Operator, BrowseNames.WellKnownRole_Operator);

            bool result = role1 != role2;
            Assert.That(result, Is.True);
        }

        [Test]
        public void RoleInequalityOperatorReturnsFalseForEqualRoles()
        {
            var role1 = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);
            var role2 = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);

            bool result = role1 != role2;
            Assert.That(result, Is.False);
        }

        [Test]
        public void RoleEqualityOperatorBothNullReturnsTrue()
        {
            Role role1 = null;
            Role role2 = null;

            bool result = role1 == role2;
            Assert.That(result, Is.True);
        }

        [Test]
        public void RoleEqualityOperatorLeftNullReturnsFalse()
        {
            Role role1 = null;
            var role2 = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);

            bool result = role1 == role2;
            Assert.That(result, Is.False);
        }

        [Test]
        public void RoleEqualityOperatorRightNullReturnsFalse()
        {
            var role1 = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);
            Role role2 = null;

            bool result = role1 == role2;
            Assert.That(result, Is.False);
        }

        [Test]
        public void RoleToStringReturnsName()
        {
            var role = new Role(ObjectIds.WellKnownRole_Observer, BrowseNames.WellKnownRole_Observer);

            Assert.That(role.ToString(), Is.EqualTo(BrowseNames.WellKnownRole_Observer));
        }

        [Test]
        public void WellKnownRolesAreDefined()
        {
            Assert.That(Role.Anonymous, Is.Not.Null);
            Assert.That(Role.AuthenticatedUser, Is.Not.Null);
            Assert.That(Role.Observer, Is.Not.Null);
            Assert.That(Role.Operator, Is.Not.Null);
            Assert.That(Role.Engineer, Is.Not.Null);
            Assert.That(Role.Supervisor, Is.Not.Null);
            Assert.That(Role.ConfigureAdmin, Is.Not.Null);
            Assert.That(Role.SecurityAdmin, Is.Not.Null);
            Assert.That(Role.TrustedApplication, Is.Not.Null);
        }

        [Test]
        public void RoleBasedIdentityWrapsInnerIdentity()
        {
            var innerMock = new Mock<IUserIdentity>();
            innerMock.Setup(i => i.DisplayName).Returns("TestUser");
            innerMock.Setup(i => i.PolicyId).Returns("policy1");
            innerMock.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            innerMock.Setup(i => i.GrantedRoleIds).Returns(default(ArrayOf<NodeId>));
            var roles = new[] { Role.Observer };
            var namespaces = new NamespaceTable();

            var identity = new RoleBasedIdentity(innerMock.Object, roles, namespaces);

            Assert.That(identity.DisplayName, Is.EqualTo("TestUser"));
            Assert.That(identity.PolicyId, Is.EqualTo("policy1"));
            Assert.That(identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            Assert.That(identity.Roles, Is.Not.Empty);
        }

        [Test]
        public void RoleBasedIdentityGrantedRoleIdsContainsRoleNodeIds()
        {
            var innerMock = new Mock<IUserIdentity>();
            innerMock.Setup(i => i.GrantedRoleIds).Returns(default(ArrayOf<NodeId>));
            var roles = new[] { Role.Observer, Role.Operator };
            var namespaces = new NamespaceTable();

            var identity = new RoleBasedIdentity(innerMock.Object, roles, namespaces);

            NodeId[] grantedIds = identity.GrantedRoleIds.ToArray();
            Assert.That(grantedIds, Is.Not.Null);
            Assert.That(grantedIds, Has.Length.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void RoleBasedIdentityWithAdditionalRolesReturnsExtendedIdentity()
        {
            var innerMock = new Mock<IUserIdentity>();
            innerMock.Setup(i => i.GrantedRoleIds).Returns(default(ArrayOf<NodeId>));
            var roles = new[] { Role.Observer };
            var namespaces = new NamespaceTable();

            var identity = new RoleBasedIdentity(innerMock.Object, roles, namespaces);
            RoleBasedIdentity extended = identity.WithAdditionalRoles(new[] { Role.Engineer }, namespaces);

            Assert.That(extended, Is.Not.Null);
            Assert.That(extended.Roles.Count(), Is.GreaterThan(identity.Roles.Count()));
        }

        [Test]
        public void RoleBasedIdentityWithRoleBasedInnerMergesRoles()
        {
            var innerMock = new Mock<IUserIdentity>();
            innerMock.Setup(i => i.GrantedRoleIds).Returns(default(ArrayOf<NodeId>));
            var namespaces = new NamespaceTable();

            var firstIdentity = new RoleBasedIdentity(innerMock.Object, new[] { Role.Observer }, namespaces);
            var wrappedIdentity = new RoleBasedIdentity(firstIdentity, new[] { Role.Engineer }, namespaces);

            Assert.That(wrappedIdentity.Roles.Count(), Is.GreaterThanOrEqualTo(2));
        }
    }
}
