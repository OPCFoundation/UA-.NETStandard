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

using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for TrustedApplication role assignment per OPC UA Part 3 §4.9.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Parallelizable]
    public class TrustedApplicationRoleTests
    {
        private Mock<IServerInternal> m_serverMock;
        private ITelemetryContext m_telemetry;
        private X509Certificate2 m_testCertificate;
        private ApplicationConfiguration m_config;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_testCertificate = CertificateBuilder
                .Create("CN=TrustedApplicationRoleTest")
                .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                .CreateForRSA();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_testCertificate?.Dispose();
        }

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

        /// <summary>
        /// Creates an <see cref="OperationContext"/> whose channel has the given security mode.
        /// </summary>
        private static OperationContext CreateOperationContext(MessageSecurityMode securityMode)
        {
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            var channelContext = new SecureChannelContext("test-channel", endpoint, RequestEncoding.Binary);
            var requestHeader = new RequestHeader();
            return new OperationContext(requestHeader, channelContext, RequestType.ActivateSession, RequestLifetime.None);
        }

        private TestableSessionManager CreateManager()
        {
            return new TestableSessionManager(m_serverMock.Object, m_config);
        }

        private Mock<ISession> CreateSessionMock(X509Certificate2 certificate)
        {
            var sessionMock = new Mock<ISession>();
            sessionMock.Setup(s => s.ClientCertificate).Returns(certificate);
            return sessionMock;
        }

        [Test]
        public void AddMandatoryRoles_WithCertAndSignMode_AddsTrustedApplicationRole()
        {
            using var manager = CreateManager();
            Mock<ISession> sessionMock = CreateSessionMock(m_testCertificate);
            OperationContext context = CreateOperationContext(MessageSecurityMode.Sign);
            using var identity = new UserIdentity();

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_TrustedApplication), Is.True,
                "Session with certificate on a signed channel should have the TrustedApplication role.");
        }

        [Test]
        public void AddMandatoryRoles_WithCertAndSignAndEncryptMode_AddsTrustedApplicationRole()
        {
            using var manager = CreateManager();
            Mock<ISession> sessionMock = CreateSessionMock(m_testCertificate);
            OperationContext context = CreateOperationContext(MessageSecurityMode.SignAndEncrypt);
            using var identity = new UserIdentity();

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_TrustedApplication), Is.True,
                "Session with certificate on a sign-and-encrypt channel should have the TrustedApplication role.");
        }

        [Test]
        public void AddMandatoryRoles_WithCertAndNoneMode_DoesNotAddTrustedApplicationRole()
        {
            using var manager = CreateManager();
            Mock<ISession> sessionMock = CreateSessionMock(m_testCertificate);
            OperationContext context = CreateOperationContext(MessageSecurityMode.None);
            using var identity = new UserIdentity();

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_TrustedApplication), Is.False,
                "Session on a None-security channel should not have the TrustedApplication role.");
        }

        [Test]
        public void AddMandatoryRoles_WithNoCertAndSignMode_DoesNotAddTrustedApplicationRole()
        {
            using var manager = CreateManager();
            Mock<ISession> sessionMock = CreateSessionMock(certificate: null);
            OperationContext context = CreateOperationContext(MessageSecurityMode.Sign);
            using var identity = new UserIdentity();

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_TrustedApplication), Is.False,
                "Session without a certificate should not have the TrustedApplication role.");
        }

        [Test]
        public void AddMandatoryRoles_WithNoCertAndNoneMode_DoesNotAddTrustedApplicationRole()
        {
            using var manager = CreateManager();
            Mock<ISession> sessionMock = CreateSessionMock(certificate: null);
            OperationContext context = CreateOperationContext(MessageSecurityMode.None);
            using var identity = new UserIdentity();

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_TrustedApplication), Is.False,
                "Session without a certificate on a None-security channel should not have the TrustedApplication role.");
        }

        [Test]
        public void AddMandatoryRoles_WithCertAndSignMode_PreservesExistingRoles()
        {
            using var manager = CreateManager();
            Mock<ISession> sessionMock = CreateSessionMock(m_testCertificate);
            OperationContext context = CreateOperationContext(MessageSecurityMode.Sign);

            // Identity that already has AuthenticatedUser role
            IUserIdentity identity = new RoleBasedIdentity(
                new UserIdentity(),
                [Role.AuthenticatedUser],
                new NamespaceTable());

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_TrustedApplication), Is.True,
                "The TrustedApplication role should be added.");
            Assert.That(result.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_AuthenticatedUser), Is.True,
                "Pre-existing roles should be preserved.");
        }

        [Test]
        public void AddMandatoryRoles_WithNoCertAndSignMode_ReturnsIdentityUnchanged()
        {
            using var manager = CreateManager();
            Mock<ISession> sessionMock = CreateSessionMock(certificate: null);
            OperationContext context = CreateOperationContext(MessageSecurityMode.Sign);
            using var identity = new UserIdentity();

            IUserIdentity result = manager.PublicAddMandatoryRoles(sessionMock.Object, context, identity);

            Assert.That(result, Is.SameAs(identity),
                "When conditions are not met, the original identity object should be returned unchanged.");
        }
    }

    /// <summary>
    /// Integration tests for TrustedApplication role using a live server fixture.
    /// These tests cover the negative path (no cert / None security) via the full session pipeline.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public class TrustedApplicationRoleIntegrationTests
    {
        private ServerFixture<StandardServer> m_fixture;
        private StandardServer m_server;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_server = m_fixture.Server;
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task SessionWithNoSecurityDoesNotHaveTrustedApplicationRoleAsync()
        {
            (RequestHeader requestHeader, _) =
                await m_server.CreateAndActivateSessionAsync("NoCertNoTrusted", useSecurity: false)
                    .ConfigureAwait(false);

            ISession session = m_server.CurrentInstance.SessionManager.GetSession(
                requestHeader.AuthenticationToken);

            Assert.That(session, Is.Not.Null);
            Assert.That(session.EffectiveIdentity.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_TrustedApplication), Is.False,
                "Session with no certificate and None security should not have the TrustedApplication role.");
        }
    }

    /// <summary>
    /// Exposes the protected <see cref="SessionManager.AddMandatoryRoles"/> method for unit testing.
    /// </summary>
    internal sealed class TestableSessionManager : SessionManager
    {
        public TestableSessionManager(IServerInternal server, ApplicationConfiguration config)
            : base(server, config)
        {
        }

        public IUserIdentity PublicAddMandatoryRoles(
            ISession session,
            OperationContext context,
            IUserIdentity effectiveIdentity)
        {
            return AddMandatoryRoles(session, context, effectiveIdentity);
        }
    }
}
