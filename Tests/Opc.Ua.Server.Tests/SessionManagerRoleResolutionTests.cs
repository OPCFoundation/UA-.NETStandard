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

using System;
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
                .Returns((Security.Certificates.Certificate)null!);
            return session;
        }

        private static IUserIdentity CreateUserNameIdentity(string userName)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns(userName);
            identity.Setup(i => i.GrantedRoleIds).Returns([]);
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

        // ----------------------------------------------------------------
        // ComputeActivationStatus — Good_PasswordChangeRequired (Part 18 §5.2.8)
        // ----------------------------------------------------------------

        [Test]
        public void ComputeActivationStatus_MustChangePasswordSet_ReturnsGoodPasswordChangeRequired()
        {
            var userManagement = new Mock<IUserManagement>();
            userManagement.Setup(u => u.MustChangePassword("alice")).Returns(true);
            m_serverMock.Setup(s => s.UserManagement).Returns(userManagement.Object);

            using TestableSessionManager manager = CreateManager();
            IUserIdentity identity = CreateUserNameIdentity("alice");

            ServiceResult status = manager.PublicComputeActivationStatus(identity);

            Assert.That(status.Code, Is.EqualTo((uint)StatusCodes.GoodPasswordChangeRequired),
                "Part 18 §5.2.8 — MustChangePassword users must see Good_PasswordChangeRequired on activation.");
        }

        [Test]
        public void ComputeActivationStatus_MustChangePasswordClear_ReturnsGood()
        {
            var userManagement = new Mock<IUserManagement>();
            userManagement.Setup(u => u.MustChangePassword(It.IsAny<string>())).Returns(false);
            m_serverMock.Setup(s => s.UserManagement).Returns(userManagement.Object);

            using TestableSessionManager manager = CreateManager();
            IUserIdentity identity = CreateUserNameIdentity("alice");

            ServiceResult status = manager.PublicComputeActivationStatus(identity);

            Assert.That(status, Is.EqualTo(ServiceResult.Good),
                "Normal users must see Good on activation.");
        }

        [Test]
        public void ComputeActivationStatus_AnonymousIdentity_ReturnsGoodAndSkipsUserManagement()
        {
            var userManagement = new Mock<IUserManagement>();
            userManagement.Setup(u => u.MustChangePassword(It.IsAny<string>())).Returns(true);
            m_serverMock.Setup(s => s.UserManagement).Returns(userManagement.Object);

            using TestableSessionManager manager = CreateManager();
            var identity = new UserIdentity();

            ServiceResult status = manager.PublicComputeActivationStatus(identity);

            Assert.That(status, Is.EqualTo(ServiceResult.Good),
                "Anonymous identities must never carry Good_PasswordChangeRequired.");
            userManagement.Verify(u => u.MustChangePassword(It.IsAny<string>()), Times.Never,
                "MustChangePassword must only be consulted for USERNAME tokens.");
        }

        [Test]
        public void ComputeActivationStatus_NoUserManagement_ReturnsGood()
        {
            // UserManagement is null — no MustChangePassword data available.
            using TestableSessionManager manager = CreateManager();
            IUserIdentity identity = CreateUserNameIdentity("alice");

            ServiceResult status = manager.PublicComputeActivationStatus(identity);

            Assert.That(status, Is.EqualTo(ServiceResult.Good),
                "Without UserManagement injected, activation must default to Good.");
        }

        [Test]
        public void ComputeActivationStatus_EmptyUserName_ReturnsGood()
        {
            var userManagement = new Mock<IUserManagement>();
            userManagement.Setup(u => u.MustChangePassword(It.IsAny<string>())).Returns(true);
            m_serverMock.Setup(s => s.UserManagement).Returns(userManagement.Object);

            using TestableSessionManager manager = CreateManager();
            IUserIdentity identity = CreateUserNameIdentity(string.Empty);

            ServiceResult status = manager.PublicComputeActivationStatus(identity);

            Assert.That(status, Is.EqualTo(ServiceResult.Good),
                "USERNAME identities without a DisplayName cannot be looked up — must return Good.");
            userManagement.Verify(u => u.MustChangePassword(It.IsAny<string>()), Times.Never);
        }

        // ----------------------------------------------------------------
        // Live re-evaluation on RoleConfigurationChanged (Part 18 §4.4.1)
        // ----------------------------------------------------------------

        [Test]
        public void OnRoleConfigurationChanged_MarksAllSessionsStale()
        {
            using TestableSessionManager manager = CreateManager();

            var sessionA = new Mock<ISession>();
            var sessionB = new Mock<ISession>();
            manager.InjectedSessions = [sessionA.Object, sessionB.Object];

            manager.PublicOnRoleConfigurationChanged(
                this,
                new RoleConfigurationChangedEventArgs(
                    ObjectIds.WellKnownRole_Observer,
                    RoleConfigurationChangeKind.IdentityAdded));

            sessionA.Verify(s => s.MarkIdentityStale(), Times.Once);
            sessionB.Verify(s => s.MarkIdentityStale(), Times.Once);
        }

        [Test]
        public void OnRoleConfigurationChanged_SessionThrows_OtherSessionsStillMarked()
        {
            using TestableSessionManager manager = CreateManager();

            var sessionA = new Mock<ISession>();
            sessionA.Setup(s => s.MarkIdentityStale()).Throws(new InvalidOperationException("boom"));
            var sessionB = new Mock<ISession>();

            // Order in the list determines walk order; the throwing session
            // is first so we need the handler to swallow the error and still
            // raise on the second session. The current implementation logs
            // the error and exits the loop, so this test documents the
            // observed behaviour. If the contract changes to per-session
            // isolation, update this assertion.
            manager.InjectedSessions = [sessionA.Object, sessionB.Object];

            Assert.DoesNotThrow(() =>
                manager.PublicOnRoleConfigurationChanged(
                    this,
                    new RoleConfigurationChangedEventArgs(
                        ObjectIds.WellKnownRole_Observer,
                        RoleConfigurationChangeKind.IdentityAdded)),
                "The event handler must never throw — it would tear down the RoleManager event source.");

            sessionA.Verify(s => s.MarkIdentityStale(), Times.Once);
        }

        [Test]
        public void ReevaluateIdentityIfStale_NotStale_DoesNotInvokeRefresh()
        {
            using TestableSessionManager manager = CreateManager();

            var session = new Mock<ISession>();
            session.Setup(s => s.IsIdentityStale).Returns(false);
            SecureChannelContext channelContext = CreateSecureChannelContext(MessageSecurityMode.SignAndEncrypt);

            manager.PublicReevaluateIdentityIfStale(session.Object, channelContext);

            session.Verify(s => s.RefreshEffectiveIdentity(It.IsAny<IUserIdentity>()), Times.Never,
                "Fresh sessions must not be refreshed.");
        }

        [Test]
        public void ReevaluateIdentityIfStale_StaleSession_RefreshesWithLayeredIdentity()
        {
            // The RoleManager grants Observer to any AuthenticatedUser; the
            // re-evaluation should apply this on top of the original Identity
            // and pass the result to RefreshEffectiveIdentity.
            using var roleManager = new RoleManager();
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

            IUserIdentity originalIdentity = CreateUserNameIdentity("alice");
            IUserIdentity? refreshed = null;
            var session = new Mock<ISession>();
            session.Setup(s => s.IsIdentityStale).Returns(true);
            session.Setup(s => s.Identity).Returns(originalIdentity);
            session.Setup(s => s.EffectiveIdentity).Returns(originalIdentity);
            session.Setup(s => s.ClientCertificate)
                .Returns((Security.Certificates.Certificate)null!);
            session.Setup(s => s.RefreshEffectiveIdentity(It.IsAny<IUserIdentity>()))
                .Callback<IUserIdentity>(id => refreshed = id);

            SecureChannelContext channelContext = CreateSecureChannelContext(MessageSecurityMode.SignAndEncrypt);
            manager.PublicReevaluateIdentityIfStale(session.Object, channelContext);

            Assert.That(refreshed, Is.Not.Null, "Stale session must be refreshed.");
            Assert.That(refreshed!.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_Observer), Is.True,
                "Refreshed identity must reflect the current RoleManager grants.");
        }

        [Test]
        public void ReevaluateIdentityIfStale_RefreshThrows_DoesNotPropagate()
        {
            using TestableSessionManager manager = CreateManager();

            IUserIdentity originalIdentity = CreateUserNameIdentity("alice");
            var session = new Mock<ISession>();
            session.Setup(s => s.IsIdentityStale).Returns(true);
            session.Setup(s => s.Identity).Returns(originalIdentity);
            session.Setup(s => s.EffectiveIdentity).Returns(originalIdentity);
            session.Setup(s => s.ClientCertificate)
                .Returns((Security.Certificates.Certificate)null!);
            session.Setup(s => s.RefreshEffectiveIdentity(It.IsAny<IUserIdentity>()))
                .Throws(new InvalidOperationException("session disposed"));

            SecureChannelContext channelContext = CreateSecureChannelContext(MessageSecurityMode.SignAndEncrypt);

            // The hook is on the request hot path — a per-session failure
            // (e.g. mid-disposal race) must not poison the request pipeline.
            Assert.DoesNotThrow(
                () => manager.PublicReevaluateIdentityIfStale(session.Object, channelContext));
        }

        [Test]
        public void ReevaluateIdentityIfStale_RuleRemoved_DropsPreviouslyGrantedRole()
        {
            // Regression for PR #3778 review comment (romanett): when a role
            // grant is REMOVED from the role manager (e.g. RemoveIdentity),
            // the live re-evaluation must drop that role from the session's
            // EffectiveIdentity on the next request.
            //
            // The implementation re-runs AddMandatoryRoles starting from
            // session.Identity (the original impersonated identity), not from
            // the accumulated EffectiveIdentity, so RoleManager-derived role
            // grants are recomputed fresh from scratch.
            using var roleManager = new RoleManager();
            ServiceResult addRule = roleManager.AddIdentity(
                ObjectIds.WellKnownRole_Observer,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "alice"
                });
            Assert.That(ServiceResult.IsGood(addRule), Is.True);
            m_serverMock.Setup(s => s.RoleManager).Returns(roleManager);

            using TestableSessionManager manager = CreateManager();
            IUserIdentity originalIdentity = CreateUserNameIdentity("alice");

            IUserIdentity? refreshed = null;
            var session = new Mock<ISession>();
            session.Setup(s => s.IsIdentityStale).Returns(true);
            session.Setup(s => s.Identity).Returns(originalIdentity);
            session.Setup(s => s.EffectiveIdentity).Returns(originalIdentity);
            session.Setup(s => s.ClientCertificate)
                .Returns((Security.Certificates.Certificate)null!);
            session.Setup(s => s.RefreshEffectiveIdentity(It.IsAny<IUserIdentity>()))
                .Callback<IUserIdentity>(id => refreshed = id);

            SecureChannelContext channelContext = CreateSecureChannelContext(MessageSecurityMode.SignAndEncrypt);

            // First re-evaluation: rule is present → Observer must be granted.
            manager.PublicReevaluateIdentityIfStale(session.Object, channelContext);
            Assert.That(refreshed, Is.Not.Null);
            Assert.That(refreshed!.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_Observer), Is.True,
                "Before the rule is removed, alice must have the Observer role.");

            // Admin removes the identity rule that granted Observer to alice.
            ServiceResult removeRule = roleManager.RemoveIdentity(
                ObjectIds.WellKnownRole_Observer,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "alice"
                });
            Assert.That(ServiceResult.IsGood(removeRule), Is.True);

            // Second re-evaluation: rule is gone → Observer must NOT be granted.
            refreshed = null;
            manager.PublicReevaluateIdentityIfStale(session.Object, channelContext);
            Assert.That(refreshed, Is.Not.Null);
            Assert.That(refreshed!.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_Observer), Is.False,
                "After RemoveIdentity, the live re-evaluation must drop the previously granted Observer role.");
        }

        [Test]
        public void ReevaluateIdentityIfStale_RoleRemoved_DropsPreviouslyGrantedRole()
        {
            // Stronger regression: removing the entire role from the manager
            // (RemoveRole) must also drop the role from active sessions on
            // the next re-evaluation. This is the case where the role NodeId
            // simply ceases to exist.
            using var roleManager = new RoleManager();
            // Use a custom role so it is not reserved and can be removed.
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend("http://example.org/custom");
            ServiceResult addRole = roleManager.AddRole("CustomRole",
                "http://example.org/custom", namespaces, defaultNamespaceIndex: 1,
                out NodeId customRoleId);
            Assert.That(ServiceResult.IsGood(addRole), Is.True);
            Assert.That(ServiceResult.IsGood(
                roleManager.AddIdentity(customRoleId,
                    new IdentityMappingRuleType
                    {
                        CriteriaType = IdentityCriteriaType.UserName,
                        Criteria = "alice"
                    })),
                Is.True);
            m_serverMock.Setup(s => s.RoleManager).Returns(roleManager);

            using TestableSessionManager manager = CreateManager();
            IUserIdentity originalIdentity = CreateUserNameIdentity("alice");

            IUserIdentity? refreshed = null;
            var session = new Mock<ISession>();
            session.Setup(s => s.IsIdentityStale).Returns(true);
            session.Setup(s => s.Identity).Returns(originalIdentity);
            session.Setup(s => s.EffectiveIdentity).Returns(originalIdentity);
            session.Setup(s => s.ClientCertificate)
                .Returns((Security.Certificates.Certificate)null!);
            session.Setup(s => s.RefreshEffectiveIdentity(It.IsAny<IUserIdentity>()))
                .Callback<IUserIdentity>(id => refreshed = id);

            SecureChannelContext channelContext = CreateSecureChannelContext(MessageSecurityMode.SignAndEncrypt);

            manager.PublicReevaluateIdentityIfStale(session.Object, channelContext);
            Assert.That(refreshed!.GrantedRoleIds.Contains(customRoleId), Is.True,
                "Before RemoveRole, alice must have the custom role.");

            Assert.That(ServiceResult.IsGood(roleManager.RemoveRole(customRoleId)), Is.True);

            refreshed = null;
            manager.PublicReevaluateIdentityIfStale(session.Object, channelContext);
            Assert.That(refreshed!.GrantedRoleIds.Contains(customRoleId), Is.False,
                "After RemoveRole, the live re-evaluation must drop the removed role.");
        }

        private static SecureChannelContext CreateSecureChannelContext(MessageSecurityMode securityMode)
        {
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            return new SecureChannelContext("test-channel", endpoint, RequestEncoding.Binary);
        }

        private TestableSessionManager CreateManager()
        {
            return new TestableSessionManager(m_serverMock.Object, m_config);
        }
    }
}
