/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
