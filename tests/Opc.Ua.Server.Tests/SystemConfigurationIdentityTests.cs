// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for <see cref="SystemConfigurationIdentity"/>.
    /// </summary>
    [TestFixture]
    [Category("Configuration")]
    [Parallelizable]
    public class SystemConfigurationIdentityTests
    {
        [Test]
        public void GrantsSecurityAdminConfigureAdminAndAuthenticatedUserRoles()
        {
            var inner = new UserIdentity();

            var identity = new SystemConfigurationIdentity(inner);

            Assert.That(identity.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_SecurityAdmin), Is.True);
            Assert.That(identity.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_ConfigureAdmin), Is.True);
            Assert.That(identity.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_AuthenticatedUser), Is.True);
        }

        [Test]
        public void ExposesTheThreePrivilegedRoles()
        {
            var identity = new SystemConfigurationIdentity(new UserIdentity());

            Assert.That(identity.Roles, Does.Contain(Role.SecurityAdmin));
            Assert.That(identity.Roles, Does.Contain(Role.ConfigureAdmin));
            Assert.That(identity.Roles, Does.Contain(Role.AuthenticatedUser));
        }

        [Test]
        public void DelegatesTokenTypeToInnerIdentity()
        {
            var inner = new UserIdentity();

            var identity = new SystemConfigurationIdentity(inner);

            Assert.That(identity.TokenType, Is.EqualTo(inner.TokenType));
            Assert.That(identity.DisplayName, Is.EqualTo(inner.DisplayName));
        }
    }
}
