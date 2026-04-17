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
using System.Text;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("Authorization")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AuthorizationHelperTests
    {
        private static readonly byte[] s_passwordBytes = Encoding.UTF8.GetBytes("password");
        private NamespaceTable m_namespaceTable;
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_namespaceTable = new NamespaceTable();
        }

        [Test]
        public void HasAuthorizationWithNullContextDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthorization(null, AuthorizationHelper.DiscoveryAdmin));
        }

        [Test]
        public void HasAuthorizationThrowsWhenUserLacksRequiredRole()
        {
            using var identity = new UserIdentity("user", s_passwordBytes);
            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.DiscoveryAdmin));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void HasAuthorizationSucceedsWhenUserHasDiscoveryAdminRole()
        {
            using var innerIdentity = new UserIdentity("admin", s_passwordBytes);
            var roles = new List<Role> { GdsRole.DiscoveryAdmin };
            var identity = new GdsRoleBasedIdentity(innerIdentity, roles, m_namespaceTable);

            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.DiscoveryAdmin));
        }

        [Test]
        public void HasAuthorizationSucceedsWithSelfAdminForOwnApplication()
        {
            var appId = new NodeId(99);
            using var innerIdentity = new UserIdentity("appuser", s_passwordBytes);
            var roles = new List<Role> { Role.AuthenticatedUser };
            var identity = new GdsRoleBasedIdentity(innerIdentity, roles, appId, m_namespaceTable);

            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.DiscoveryAdminOrSelfAdmin,
                    appId));
        }

        [Test]
        public void HasAuthorizationThrowsWithSelfAdminForDifferentApplication()
        {
            var ownAppId = new NodeId(99);
            var otherAppId = new NodeId(100);
            using var innerIdentity = new UserIdentity("appuser", s_passwordBytes);
            var roles = new List<Role> { Role.AuthenticatedUser };
            var identity = new GdsRoleBasedIdentity(innerIdentity, roles, ownAppId, m_namespaceTable);

            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.DiscoveryAdminOrSelfAdmin,
                    otherAppId));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void HasAuthorizationThrowsWithSelfAdminAndNullApplicationId()
        {
            var appId = new NodeId(99);
            using var innerIdentity = new UserIdentity("appuser", s_passwordBytes);
            var roles = new List<Role> { Role.AuthenticatedUser };
            var identity = new GdsRoleBasedIdentity(innerIdentity, roles, appId, m_namespaceTable);

            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.DiscoveryAdminOrSelfAdmin,
                    NodeId.Null));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void HasAuthorizationThrowsForAnonymousUser()
        {
            using var identity = new UserIdentity();
            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.CertificateAuthorityAdmin));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void HasAuthorizationSucceedsWithCertificateAuthorityAdminRole()
        {
            using var innerIdentity = new UserIdentity("caadmin", s_passwordBytes);
            var roles = new List<Role> { GdsRole.CertificateAuthorityAdmin };
            var identity = new GdsRoleBasedIdentity(innerIdentity, roles, m_namespaceTable);

            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.CertificateAuthorityAdmin));
        }

        [Test]
        public void HasAuthenticatedSecureChannelDoesNotThrowForNonSystemContext()
        {
            var context = new SessionSystemContext(m_telemetry)
            {
                NamespaceUris = m_namespaceTable
            };

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthenticatedSecureChannel(context));
        }

        [Test]
        public void HasAuthenticatedSecureChannelDoesNotThrowForNullContext()
        {
            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthenticatedSecureChannel(null));
        }

        [Test]
        public void HasAuthenticatedSecureChannelThrowsForNonSecureChannel()
        {
            var endpoint = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None
            };
            var channelContext = new SecureChannelContext(
                "test-channel", endpoint, RequestEncoding.Binary);
            var requestHeader = new RequestHeader();
            var operationContext = new OperationContext(
                requestHeader, channelContext, RequestType.Read, RequestLifetime.None);
            var context = new SystemContext(operationContext, m_telemetry)
            {
                NamespaceUris = m_namespaceTable
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                AuthorizationHelper.HasAuthenticatedSecureChannel(context));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void HasAuthenticatedSecureChannelDoesNotThrowForSignAndEncrypt()
        {
            var endpoint = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt
            };
            var channelContext = new SecureChannelContext(
                "test-channel", endpoint, RequestEncoding.Binary);
            var requestHeader = new RequestHeader();
            var operationContext = new OperationContext(
                requestHeader, channelContext, RequestType.Read, RequestLifetime.None);
            var context = new SystemContext(operationContext, m_telemetry)
            {
                NamespaceUris = m_namespaceTable
            };

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthenticatedSecureChannel(context));
        }

        [Test]
        public void StaticRoleListsAreCorrectlyPopulated()
        {
            Assert.That(AuthorizationHelper.AuthenticatedUser, Has.Count.EqualTo(1));
            Assert.That(AuthorizationHelper.AuthenticatedUser[0], Is.EqualTo(Role.AuthenticatedUser));

            Assert.That(AuthorizationHelper.DiscoveryAdmin, Has.Count.EqualTo(1));
            Assert.That(AuthorizationHelper.DiscoveryAdmin[0], Is.EqualTo(GdsRole.DiscoveryAdmin));

            Assert.That(AuthorizationHelper.DiscoveryAdminOrSelfAdmin, Has.Count.EqualTo(2));
            Assert.That(AuthorizationHelper.DiscoveryAdminOrSelfAdmin,
                Does.Contain(GdsRole.DiscoveryAdmin));
            Assert.That(AuthorizationHelper.DiscoveryAdminOrSelfAdmin,
                Does.Contain(GdsRole.ApplicationSelfAdmin));

            Assert.That(AuthorizationHelper.AuthenticatedUserOrSelfAdmin, Has.Count.EqualTo(2));
            Assert.That(AuthorizationHelper.AuthenticatedUserOrSelfAdmin,
                Does.Contain(Role.AuthenticatedUser));
            Assert.That(AuthorizationHelper.AuthenticatedUserOrSelfAdmin,
                Does.Contain(GdsRole.ApplicationSelfAdmin));

            Assert.That(AuthorizationHelper.CertificateAuthorityAdminOrSelfAdmin, Has.Count.EqualTo(2));
            Assert.That(AuthorizationHelper.CertificateAuthorityAdminOrSelfAdmin,
                Does.Contain(GdsRole.CertificateAuthorityAdmin));
            Assert.That(AuthorizationHelper.CertificateAuthorityAdminOrSelfAdmin,
                Does.Contain(GdsRole.ApplicationSelfAdmin));

            Assert.That(AuthorizationHelper.CertificateAuthorityAdmin, Has.Count.EqualTo(1));
            Assert.That(AuthorizationHelper.CertificateAuthorityAdmin[0],
                Is.EqualTo(GdsRole.CertificateAuthorityAdmin));
        }

        [Test]
        public void HasAuthorizationSucceedsWithAuthenticatedUserRole()
        {
            using var innerIdentity = new UserIdentity("user", s_passwordBytes);
            var roles = new List<Role> { Role.AuthenticatedUser };
            var identity = new RoleBasedIdentity(innerIdentity, roles, m_namespaceTable);

            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.AuthenticatedUser));
        }

        [Test]
        public void HasAuthorizationThrowsWhenNonSelfAdminIdentityAccessesSelfAdminOnlyList()
        {
            using var innerIdentity = new UserIdentity("user", s_passwordBytes);
            var roles = new List<Role> { Role.Observer };
            var identity = new RoleBasedIdentity(innerIdentity, roles, m_namespaceTable);

            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.CertificateAuthorityAdminOrSelfAdmin,
                    new NodeId(42)));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void HasAuthorizationWithCertAuthorityAdminOrSelfAdminSucceedsForCaAdmin()
        {
            using var innerIdentity = new UserIdentity("caadmin", s_passwordBytes);
            var roles = new List<Role> { GdsRole.CertificateAuthorityAdmin };
            var identity = new GdsRoleBasedIdentity(innerIdentity, roles, m_namespaceTable);

            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.CertificateAuthorityAdminOrSelfAdmin,
                    new NodeId(42)));
        }
    }
}
