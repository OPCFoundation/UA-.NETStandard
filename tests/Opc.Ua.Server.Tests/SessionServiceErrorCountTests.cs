/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that a service request the service rejects after the Session admitted it
    /// counts as an error of the Session (OPC 10000-5 12.11, 12.13).
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("Session")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class SessionServiceErrorCountTests
    {
        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_secureChannelContext;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true
            };
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [SetUp]
        public async Task SetUpAsync()
        {
            (m_requestHeader, m_secureChannelContext) = await m_server.CreateAndActivateSessionAsync(
                TestContext.CurrentContext.Test.Name).ConfigureAwait(false);
            m_requestHeader.TimeoutHint = 10000;
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            await m_server.CloseSessionAsync(m_secureChannelContext, m_requestHeader, true, RequestLifetime.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// A Read without nodes is rejected with Bad_NothingToDo: it counts as a Read
        /// request and as an error of the Session, but not as an unauthorized request.
        /// </summary>
        [Test]
        public async Task RejectedReadCountsAsSessionErrorAsync()
        {
            ISession session = m_server.CurrentInstance.SessionManager.GetSessions()
                .Single(s => s.SessionName == TestContext.CurrentContext.Test.Name);
            (uint Total, uint Errors, uint ReadTotal, uint ReadErrors, uint Unauthorized) before =
                ReadCounts(session);

            m_requestHeader.Timestamp = DateTimeUtc.Now;
            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_server.ReadAsync(
                    m_secureChannelContext,
                    m_requestHeader,
                    0,
                    TimestampsToReturn.Neither,
                    [],
                    RequestLifetime.None).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));

            (uint Total, uint Errors, uint ReadTotal, uint ReadErrors, uint Unauthorized) after =
                ReadCounts(session);
            Assert.That(after.ReadTotal - before.ReadTotal, Is.EqualTo(1u));
            Assert.That(after.ReadErrors - before.ReadErrors, Is.EqualTo(1u));
            Assert.That(after.Total - before.Total, Is.EqualTo(1u));
            Assert.That(after.Errors - before.Errors, Is.EqualTo(1u));
            Assert.That(after.Unauthorized, Is.EqualTo(before.Unauthorized));
        }

        private static (uint Total, uint Errors, uint ReadTotal, uint ReadErrors, uint Unauthorized) ReadCounts(
            ISession session)
        {
            return session.ReadDiagnostics(d => (
                d.TotalRequestCount.TotalCount,
                d.TotalRequestCount.ErrorCount,
                d.ReadCount.TotalCount,
                d.ReadCount.ErrorCount,
                d.UnauthorizedRequestCount));
        }
    }
}
