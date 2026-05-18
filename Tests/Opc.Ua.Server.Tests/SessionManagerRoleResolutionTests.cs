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

#nullable enable

using Moq;
using NUnit.Framework;
using Opc.Ua.Server.UserManagement;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for <see cref="SessionManager.AddMandatoryRoles"/> covering the
    /// integration with <see cref="IRoleManager.ResolveGrantedRoles"/>
    /// (Part 18 §4.4.4) and the <see cref="IUserManagement.MustChangePassword"/>
    /// gate (Part 18 §5.2.8).
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Parallelizable]
    public class SessionManagerRoleResolutionTests
    {
        private Mock<IServerInternal> m_serverMock = null!;
        private ITelemetryContext m_telemetry = null!;
        private ApplicationConfiguration m_config = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_serverMock = new Mock<IServerInternal>();
            m_serverMock.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());

            m_config = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 3_600_000,
                    MaxSessionCount = 100,
                    MaxRequestAge = 60_000,
                    MaxBrowseContinuationPoints = 10,
                    MaxHistoryContinuationPoints = 10
                }
            };
        }

        private static OperationContext CreateOperationContext(MessageSecurityMode securityMode)
        {
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            var channelContext = new SecureChannelContext("test-channel", endpoint, RequestEncoding.Binary);
            var requestHeader = new RequestHeader();
            return new OperationContext(requestHeader, channelContext, RequestType.ActivateSession, RequestLifetime.None);
        }

        private static Mock<ISession> CreateSessionMock()
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.ClientCertificate)
                .Returns((Opc.Ua.Security.Certificates.Certificate)null!);
            return session;
        }

        private static IUserIdentity CreateUserNameIdentity(string userName)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns(userName);
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Empty<NodeId>());
            return identity.Object;
        }

        // ----------------------------------------------------------------
        // RoleManager.ResolveGrantedRoles integration (Part 18 §4.4.4)
        // ----------------------------------------------------------------

        [Test]
        public void AddMandatoryRoles_RoleManagerGrantsRole_AddsToEffectiveIdentity()
        {
            using var roleManager = new RoleManager();
            // Configure the Observer role to grant any AuthenticatedUser.
            Assert.That(ServiceResult.IsGood(
                roleManager.AddIdentity(
                    ObjectIds.WellKnownRole_Observer,
                    new IdentityMappingRuleType
                    {
                        CriteriaType = IdentityCriteriaType.AuthenticatedUser
                    })),
                Is.True);
            m_serverMock.Setup(s => s.RoleManager).Returns(roleManager);

            using TestableSessionManager manager = CreateManager();
            IUserIdentity identity = CreateUserNameIdentity("alice");
            Mock<ISession> sessionMock = CreateSessionMock();
            OperationContext context = CreateOperationContext(MessageSecurityMode.SignAndEncrypt);

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_Observer), Is.True,
                "RoleManager.ResolveGrantedRoles should have layered the Observer role onto the session.");
        }

        [Test]
        public void AddMandatoryRoles_NoMatchingRule_DoesNotAddDynamicRoles()
        {
            using var roleManager = new RoleManager();
            // No identity rules added — Anonymous role still matches anonymous tokens by default.
            m_serverMock.Setup(s => s.RoleManager).Returns(roleManager);

            using TestableSessionManager manager = CreateManager();
            IUserIdentity identity = CreateUserNameIdentity("alice");
            Mock<ISession> sessionMock = CreateSessionMock();
            OperationContext context = CreateOperationContext(MessageSecurityMode.SignAndEncrypt);

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            // AuthenticatedUser default rule on the AuthenticatedUser role matches any non-anonymous session.
            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_AuthenticatedUser), Is.True,
                "Default AuthenticatedUser rule should grant the AuthenticatedUser role to USERNAME sessions.");
            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_Observer), Is.False);
            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_Operator), Is.False);
        }

        // ----------------------------------------------------------------
        // MustChangePassword integration (Part 18 §5.2.8)
        // ----------------------------------------------------------------

        [Test]
        public void AddMandatoryRoles_MustChangePasswordSet_RestrictsSessionToAnonymousRole()
        {
            var userManagement = new Mock<IUserManagement>();
            userManagement.Setup(u => u.MustChangePassword("alice")).Returns(true);
            m_serverMock.Setup(s => s.UserManagement).Returns(userManagement.Object);

            using TestableSessionManager manager = CreateManager();
            IUserIdentity identity = CreateUserNameIdentity("alice");
            Mock<ISession> sessionMock = CreateSessionMock();
            OperationContext context = CreateOperationContext(MessageSecurityMode.SignAndEncrypt);

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_Anonymous), Is.True,
                "MustChangePassword users must only get the Anonymous role.");
            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_Observer), Is.False,
                "MustChangePassword must short-circuit the role-manager layer.");
        }

        [Test]
        public void AddMandatoryRoles_MustChangePasswordClear_GrantsNormalRoles()
        {
            var userManagement = new Mock<IUserManagement>();
            userManagement.Setup(u => u.MustChangePassword(It.IsAny<string>())).Returns(false);
            m_serverMock.Setup(s => s.UserManagement).Returns(userManagement.Object);

            using var roleManager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                roleManager.AddIdentity(
                    ObjectIds.WellKnownRole_Observer,
                    new IdentityMappingRuleType
                    {
                        CriteriaType = IdentityCriteriaType.UserName,
                        Criteria = "alice"
                    })),
                Is.True);
            m_serverMock.Setup(s => s.RoleManager).Returns(roleManager);

            using TestableSessionManager manager = CreateManager();
            IUserIdentity identity = CreateUserNameIdentity("alice");
            Mock<ISession> sessionMock = CreateSessionMock();
            OperationContext context = CreateOperationContext(MessageSecurityMode.SignAndEncrypt);

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_Observer), Is.True,
                "When MustChangePassword is false, the role manager should grant the configured role.");
        }

        [Test]
        public void AddMandatoryRoles_NoUserManagement_SkipsMustChangePasswordCheck()
        {
            // UserManagement is null - MustChangePassword check short-circuits.
            using TestableSessionManager manager = CreateManager();
            IUserIdentity identity = CreateUserNameIdentity("alice");
            Mock<ISession> sessionMock = CreateSessionMock();
            OperationContext context = CreateOperationContext(MessageSecurityMode.SignAndEncrypt);

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            // The identity should NOT be wrapped in an Anonymous-only restriction.
            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_Anonymous), Is.False,
                "Without UserManagement injected, MustChangePassword path must not activate.");
        }

        [Test]
        public void AddMandatoryRoles_MustChangePasswordOnAnonymousToken_NoEffect()
        {
            // Anonymous identity should never be restricted by MustChangePassword.
            var userManagement = new Mock<IUserManagement>();
            userManagement.Setup(u => u.MustChangePassword(It.IsAny<string>())).Returns(true);
            m_serverMock.Setup(s => s.UserManagement).Returns(userManagement.Object);

            using TestableSessionManager manager = CreateManager();
            var identity = new UserIdentity();
            Mock<ISession> sessionMock = CreateSessionMock();
            OperationContext context = CreateOperationContext(MessageSecurityMode.SignAndEncrypt);

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            // Anonymous identity should not trigger the MustChangePassword path
            // (which is USERNAME-only per the spec).
            userManagement.Verify(u => u.MustChangePassword(It.IsAny<string>()), Times.Never,
                "MustChangePassword should only be consulted for USERNAME tokens.");
        }

        private TestableSessionManager CreateManager()
        {
            return new TestableSessionManager(m_serverMock.Object, m_config);
        }
    }
}
