// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Identity")]
    public class RoleBasedIdentityTests
    {
        [Test]
        public void RoleEqualityOperatorsAndToStringUseNameAndRoleId()
        {
            var role = new Role(new ExpandedNodeId(new NodeId(5001)), "Custom");
            var same = new Role(new ExpandedNodeId(new NodeId(5001)), "Custom");
            var different = new Role(new ExpandedNodeId(new NodeId(5002)), "Other");
            var derived = new DerivedRole(new ExpandedNodeId(new NodeId(5001)), "Custom");
            Role nullRole = null!;

            Assert.That(role, Is.Not.EqualTo(nullRole));
            Assert.That(role, Is.Not.EqualTo(derived));
            Assert.That(role, Is.EqualTo(same));
            Assert.That(role.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(role.ToString(), Is.EqualTo("Custom"));
            Assert.That(AreEqual(nullRole, nullRole), Is.True);
            Assert.That(AreEqual(nullRole, role), Is.False);
            Assert.That(AreNotEqual(role, different), Is.True);
        }

        [Test]
        public void RoleBasedIdentityMergesNestedRolesAndForwardsInnerIdentityProperties()
        {
            NamespaceTable namespaces = CreateNamespaceTable();
            var inner = new UserIdentity("operator", []);
            var first = new RoleBasedIdentity(inner, [Role.Observer], namespaces);

            RoleBasedIdentity second = first.WithAdditionalRoles(
                [Role.Operator],
                namespaces);

            Assert.That(second.Roles.Select(r => r.Name), Does.Contain(Role.Observer.Name));
            Assert.That(second.Roles.Select(r => r.Name), Does.Contain(Role.Operator.Name));
            Assert.That(
                second.GrantedRoleIds.ToArray(),
                Does.Contain(ExpandedNodeId.ToNodeId(Role.Observer.RoleId, namespaces)));
            Assert.That(
                second.GrantedRoleIds.ToArray(),
                Does.Contain(ExpandedNodeId.ToNodeId(Role.Operator.RoleId, namespaces)));
            Assert.That(second.DisplayName, Is.EqualTo(inner.DisplayName));
            Assert.That(second.PolicyId, Is.EqualTo(inner.PolicyId));
            Assert.That(second.TokenType, Is.EqualTo(inner.TokenType));
            Assert.That(second.IssuedTokenType, Is.EqualTo(inner.IssuedTokenType));
            Assert.That(second.SupportsSignatures, Is.EqualTo(inner.SupportsSignatures));
            Assert.That(second.TokenHandler, Is.SameAs(inner.TokenHandler));
        }

        private static NamespaceTable CreateNamespaceTable()
        {
            var namespaces = new NamespaceTable();
            namespaces.Append(Ua.Namespaces.OpcUa);
            return namespaces;
        }

        private static bool AreEqual(Role lhs, Role rhs)
        {
            return lhs == rhs;
        }

        private static bool AreNotEqual(Role lhs, Role rhs)
        {
            return lhs != rhs;
        }

        private sealed class DerivedRole : Role
        {
            public DerivedRole(ExpandedNodeId roleId, string name)
                : base(roleId, name)
            {
            }
        }
    }
}
