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
using System.Security.Cryptography;
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
            // Ensure the GDS namespace is registered so that GDS-namespace
            // role NodeIds (e.g. WellKnownRole_DiscoveryAdmin) resolve to a
            // unique NodeId rather than colliding on NodeId.Null with the
            // namespace-less ApplicationSelfAdmin/ApplicationAdmin
            // privileges.
            m_namespaceTable.Append(Namespaces.OpcUaGds);
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
            var identity = new UserIdentity("user", s_passwordBytes);
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
            var innerIdentity = new UserIdentity("admin", s_passwordBytes);
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
            var innerIdentity = new UserIdentity("appuser", s_passwordBytes);
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
            var innerIdentity = new UserIdentity("appuser", s_passwordBytes);
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
            var innerIdentity = new UserIdentity("appuser", s_passwordBytes);
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
            var identity = new UserIdentity();
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
            var innerIdentity = new UserIdentity("caadmin", s_passwordBytes);
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
        public void HasAuthorizationSucceedsWithKeyCredentialAdminRole()
        {
            var innerIdentity = new UserIdentity("keyadmin", s_passwordBytes);
            var roles = new List<Role> { GdsRole.KeyCredentialAdmin };
            var identity = new GdsRoleBasedIdentity(innerIdentity, roles, m_namespaceTable);

            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.KeyCredentialAdmin));
        }

        [Test]
        public void KeyCredentialAuthorizationAllowsSelfAdminOnlyForOwnApplication()
        {
            var ownAppId = new NodeId(99);
            var otherAppId = new NodeId(100);
            var identity = new GdsRoleBasedIdentity(
                new UserIdentity("appuser", s_passwordBytes),
                [GdsRole.ApplicationSelfAdmin],
                ownAppId,
                m_namespaceTable);
            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.KeyCredentialAdminOrSelfAdminOrAppAdmin,
                    ownAppId));
            Assert.That(
                () => AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.KeyCredentialAdminOrSelfAdminOrAppAdmin,
                    otherAppId),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void KeyCredentialAuthorizationAllowsApplicationAdminOnlyForManagedApplication()
        {
            var managedAppId = new NodeId(11);
            var unmanagedAppId = new NodeId(12);
            var identity = new GdsRoleBasedIdentity(
                new UserIdentity("agent", s_passwordBytes),
                [GdsRole.ApplicationAdmin],
                NodeId.Null,
                [managedAppId],
                m_namespaceTable);
            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.KeyCredentialAdminOrSelfAdminOrAppAdmin,
                    managedAppId));
            Assert.That(
                () => AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.KeyCredentialAdminOrSelfAdminOrAppAdmin,
                    unmanagedAppId),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void HasAuthenticatedSecureChannelThrowsForNonSystemContext()
        {
            var context = new SessionSystemContext(m_telemetry)
            {
                NamespaceUris = m_namespaceTable
            };

            Assert.That(
                () => AuthorizationHelper.HasAuthenticatedSecureChannel(context),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public void HasAuthenticatedSecureChannelThrowsForNullContext()
        {
            Assert.That(
                () => AuthorizationHelper.HasAuthenticatedSecureChannel(null),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityModeInsufficient));
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

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public void HasAuthenticatedSecureChannelDoesNotThrowForSign()
        {
            var endpoint = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.Sign
            };
            var channelContext = new SecureChannelContext(
                "test-channel", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(), channelContext, RequestType.Read, RequestLifetime.None);
            var context = new SystemContext(operationContext, m_telemetry)
            {
                NamespaceUris = m_namespaceTable
            };

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthenticatedSecureChannel(context));
        }

        [Test]
        public void HasAuthenticatedSecureChannelWithRequireEncryptionThrowsForSign()
        {
            var endpoint = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.Sign
            };
            var channelContext = new SecureChannelContext(
                "test-channel", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(), channelContext, RequestType.Read, RequestLifetime.None);
            var context = new SystemContext(operationContext, m_telemetry)
            {
                NamespaceUris = m_namespaceTable
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
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
        public void GetClientCertificateFingerprintReturnsSha256()
        {
            byte[] clientCertificate = [1, 2, 3, 4, 5];
            var endpoint = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt
            };
            var channelContext = new SecureChannelContext(
                "test-channel",
                endpoint,
                RequestEncoding.Binary,
                clientCertificate);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None);
            var context = new SystemContext(operationContext, m_telemetry)
            {
                NamespaceUris = m_namespaceTable
            };

#if NET6_0_OR_GREATER
            byte[] expected = SHA256.HashData(clientCertificate);
#else
            byte[] expected;
            using (SHA256 sha256 = SHA256.Create())
            {
                expected = sha256.ComputeHash(clientCertificate);
            }
#endif

            ByteString fingerprint = AuthorizationHelper.GetClientCertificateFingerprint(context);

            Assert.That(fingerprint.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void GetClientCertificateFingerprintThrowsWhenCertificateIsMissing()
        {
            var endpoint = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt
            };
            var channelContext = new SecureChannelContext(
                "test-channel",
                endpoint,
                RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None);
            var context = new SystemContext(operationContext, m_telemetry)
            {
                NamespaceUris = m_namespaceTable
            };

            Assert.That(
                () => AuthorizationHelper.GetClientCertificateFingerprint(context),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void StaticRoleListsAreCorrectlyPopulated()
        {
            Assert.That(AuthorizationHelper.AuthenticatedUser, Has.Count.EqualTo(1));
            Assert.That(AuthorizationHelper.AuthenticatedUser[0], Is.EqualTo(Role.AuthenticatedUser));

            Assert.That(AuthorizationHelper.DiscoveryAdmin, Has.Count.EqualTo(1));
            Assert.That(AuthorizationHelper.DiscoveryAdmin[0], Is.EqualTo(GdsRole.DiscoveryAdmin));

            Assert.That(AuthorizationHelper.KeyCredentialAdmin, Has.Count.EqualTo(1));
            Assert.That(AuthorizationHelper.KeyCredentialAdmin[0],
                Is.EqualTo(GdsRole.KeyCredentialAdmin));

            Assert.That(
                AuthorizationHelper.KeyCredentialAdminOrSelfAdminOrAppAdmin,
                Is.EquivalentTo(
                [
                    GdsRole.KeyCredentialAdmin,
                    GdsRole.ApplicationSelfAdmin,
                    GdsRole.ApplicationAdmin
                ]));

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
            var innerIdentity = new UserIdentity("user", s_passwordBytes);
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
            var innerIdentity = new UserIdentity("user", s_passwordBytes);
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
            var innerIdentity = new UserIdentity("caadmin", s_passwordBytes);
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

        [Test]
        public void HasAuthorizationWithApplicationAdminSucceedsForAdministeredApp()
        {
            var managedApp1 = new NodeId(11);
            var managedApp2 = new NodeId(12);
            var innerIdentity = new UserIdentity("agent", s_passwordBytes);
            var identity = new GdsRoleBasedIdentity(
                innerIdentity,
                [GdsRole.ApplicationAdmin],
                NodeId.Null,
                [managedApp1, managedApp2],
                m_namespaceTable);
            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.DiscoveryAdminOrSelfAdminOrAppAdmin,
                    managedApp2));

            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                    managedApp1));
        }

        [Test]
        public void HasAuthorizationWithApplicationAdminThrowsForUnmanagedApp()
        {
            var managedApp = new NodeId(11);
            var unmanagedApp = new NodeId(99);
            var innerIdentity = new UserIdentity("agent", s_passwordBytes);
            var identity = new GdsRoleBasedIdentity(
                innerIdentity,
                [GdsRole.ApplicationAdmin],
                NodeId.Null,
                [managedApp],
                m_namespaceTable);
            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.DiscoveryAdminOrSelfAdminOrAppAdmin,
                    unmanagedApp));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void HasAuthorizationWithApplicationAdminSucceedsForRegisterApplication()
        {
            var managedApp = new NodeId(11);
            var innerIdentity = new UserIdentity("agent", s_passwordBytes);
            var identity = new GdsRoleBasedIdentity(
                innerIdentity,
                [GdsRole.ApplicationAdmin],
                NodeId.Null,
                [managedApp],
                m_namespaceTable);
            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            // RegisterApplication has no existing applicationId; the
            // ApplicationAdmin privilege alone is sufficient.
            Assert.DoesNotThrow(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.DiscoveryAdminOrAppAdmin));
        }

        [Test]
        public void HasAuthorizationWithApplicationAdminThrowsWithEmptyAdministeredList()
        {
            var innerIdentity = new UserIdentity("agent", s_passwordBytes);
            var identity = new GdsRoleBasedIdentity(
                innerIdentity,
                [GdsRole.ApplicationAdmin],
                NodeId.Null,
                administeredApplicationIds: null,
                m_namespaceTable);
            var context = new SessionSystemContext(m_telemetry)
            {
                UserIdentity = identity,
                NamespaceUris = m_namespaceTable
            };

            // Without any administered apps the privilege is inert.
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.DiscoveryAdminOrAppAdmin));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void NewRoleListsAreCorrectlyPopulated()
        {
            Assert.That(AuthorizationHelper.DiscoveryAdminOrAppAdmin, Has.Count.EqualTo(2));
            Assert.That(AuthorizationHelper.DiscoveryAdminOrAppAdmin,
                Does.Contain(GdsRole.DiscoveryAdmin));
            Assert.That(AuthorizationHelper.DiscoveryAdminOrAppAdmin,
                Does.Contain(GdsRole.ApplicationAdmin));

            Assert.That(AuthorizationHelper.DiscoveryAdminOrSelfAdminOrAppAdmin, Has.Count.EqualTo(3));
            Assert.That(AuthorizationHelper.DiscoveryAdminOrSelfAdminOrAppAdmin,
                Does.Contain(GdsRole.DiscoveryAdmin));
            Assert.That(AuthorizationHelper.DiscoveryAdminOrSelfAdminOrAppAdmin,
                Does.Contain(GdsRole.ApplicationSelfAdmin));
            Assert.That(AuthorizationHelper.DiscoveryAdminOrSelfAdminOrAppAdmin,
                Does.Contain(GdsRole.ApplicationAdmin));

            Assert.That(AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                Has.Count.EqualTo(3));
            Assert.That(AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                Does.Contain(GdsRole.CertificateAuthorityAdmin));
            Assert.That(AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                Does.Contain(GdsRole.ApplicationSelfAdmin));
            Assert.That(AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                Does.Contain(GdsRole.ApplicationAdmin));
        }
    }
}
