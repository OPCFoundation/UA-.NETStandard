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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Assert;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// CreateSession when the number of Sessions has reached MaxSessionCount.
    /// </summary>
    /// <remarks>
    /// OPC 10000-4 v1.05.07 5.7.2.1: "the Server shall close the oldest Session that is
    /// not activated before reaching the maximum number of supported Sessions."
    /// Unless noted, requests are sent over opc.tcp.
    /// </remarks>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class SessionCapNonActivatedTests
    {
        private const double kSessionTimeout = ServerFixtureUtils.DefaultSessionTimeout;

        private ITelemetryContext m_telemetry;
        private ServerFixture<StandardServer> m_fixture;
        private StandardServer m_server;
        private List<IDisposable> m_clients;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_clients = [];
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            foreach (IDisposable client in m_clients)
            {
                Utils.SilentDispose(client);
            }
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
                m_fixture = null;
            }
        }

        /// <summary>
        /// A Client can create and activate a Session at the limit while there are Sessions
        /// that are not activated, whether the channel that created them is open or not.
        /// </summary>
        [TestCase(5, false)]
        [TestCase(5, true)]
        [TestCase(75, false)]
        public async Task SessionIsCreatedAtTheLimitAsync(
            int maxSessionCount,
            bool closeFirstChannel)
        {
            await StartServerAsync(maxSessionCount).ConfigureAwait(false);

            SessionClient firstClient = await OpenChannelAsync().ConfigureAwait(false);
            for (int ii = 0; ii < maxSessionCount; ii++)
            {
                CreateSessionResponse response =
                    await CreateSessionAsync(firstClient, $"Session{ii}").ConfigureAwait(false);
                Assert.That(response.RevisedSessionTimeout, Is.EqualTo(kSessionTimeout));
            }
            Assert.That(SessionCount, Is.EqualTo(maxSessionCount));

            if (closeFirstChannel)
            {
                await firstClient.CloseAsync().ConfigureAwait(false);
            }

            SessionClient lastClient = await OpenChannelAsync().ConfigureAwait(false);
            await CreateAndActivateSessionAsync(lastClient, "Newest").ConfigureAwait(false);

            Assert.That(SessionCount, Is.LessThanOrEqualTo(maxSessionCount));
        }

        /// <summary>
        /// The oldest Session that is not activated is the one to close. An activated
        /// Session is left alone even if it is older, and so are newer Sessions.
        /// </summary>
        [Test]
        public async Task OnlyTheOldestNonActivatedSessionIsClosedAsync()
        {
            await StartServerAsync(4).ConfigureAwait(false);

            SessionClient client = await OpenChannelAsync().ConfigureAwait(false);
            NodeId activated = await CreateAndActivateSessionAsync(client, "Activated").ConfigureAwait(false);

            var notActivated = new List<NodeId>();
            SessionClient firstClient = await OpenChannelAsync().ConfigureAwait(false);
            for (int ii = 0; ii < 3; ii++)
            {
                CreateSessionResponse response =
                    await CreateSessionAsync(firstClient, $"Session{ii}").ConfigureAwait(false);
                notActivated.Add(response.AuthenticationToken);

                // keep the connection times apart, the system clock may be coarse.
                await Task.Delay(50).ConfigureAwait(false);
            }

            SessionClient lastClient = await OpenChannelAsync().ConfigureAwait(false);
            NodeId newest = await CreateAndActivateSessionAsync(lastClient, "Newest").ConfigureAwait(false);

            Assert.That(Exists(activated), Is.True, "The activated Session must not be closed.");
            Assert.That(Exists(notActivated[0]), Is.False, "The oldest Session must be closed.");
            Assert.That(Exists(notActivated[1]), Is.True, "A newer Session must not be closed.");
            Assert.That(Exists(notActivated[2]), Is.True, "A newer Session must not be closed.");
            Assert.That(Exists(newest), Is.True);
        }

        /// <summary>
        /// A table filled with activated Sessions is a real limit: nothing may be closed
        /// and CreateSession returns Bad_TooManySessions (OPC 10000-4 5.7.2.3).
        /// </summary>
        [Test]
        public async Task ActivatedSessionsStillReachTheLimitAsync()
        {
            const int maxSessionCount = 3;
            await StartServerAsync(maxSessionCount).ConfigureAwait(false);

            var activated = new List<NodeId>();
            for (int ii = 0; ii < maxSessionCount; ii++)
            {
                SessionClient client = await OpenChannelAsync().ConfigureAwait(false);
                activated.Add(
                    await CreateAndActivateSessionAsync(client, $"Activated{ii}").ConfigureAwait(false));
            }

            SessionClient rejected = await OpenChannelAsync().ConfigureAwait(false);
            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                () => CreateSessionAsync(rejected, "Rejected"));

            Assert.That((StatusCode)sre.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTooManySessions));
            Assert.That(activated, Has.All.Matches<NodeId>(Exists));
        }

        /// <summary>
        /// A Session whose activation is in progress is not closed, even though it is not
        /// activated yet. When the activation fails, the Session can be closed again.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task SessionWithActivationInProgressIsNotClosedAsync(bool activationSucceeds)
        {
            await StartServerAsync(1).ConfigureAwait(false);

            using var entered = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            int calls = 0;
            m_server.CurrentInstance.SessionManager.ImpersonateUser += (_, args) =>
            {
                // only the first activation is held back and possibly rejected.
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.Set();
                    resume.Wait(TimeSpan.FromSeconds(20));
                    if (!activationSucceeds)
                    {
                        args.IdentityValidationError = StatusCodes.BadIdentityTokenRejected;
                    }
                }
            };

            SessionClient client = await OpenChannelAsync().ConfigureAwait(false);
            CreateSessionResponse pending = await CreateSessionAsync(client, "Pending").ConfigureAwait(false);
            SessionClient other = await OpenChannelAsync().ConfigureAwait(false);

            Task activation = ActivateSessionAsync(client, pending.AuthenticationToken);
            try
            {
                Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);

                ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                    () => CreateSessionAsync(other, "Other"));
                Assert.That((StatusCode)sre.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTooManySessions));
                Assert.That(Exists(pending.AuthenticationToken), Is.True);
            }
            finally
            {
                resume.Set();
            }

            if (activationSucceeds)
            {
                await activation.ConfigureAwait(false);
                ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                    () => CreateSessionAsync(other, "Other"));
                Assert.That((StatusCode)sre.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTooManySessions));
                Assert.That(Exists(pending.AuthenticationToken), Is.True);
            }
            else
            {
                ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(() => activation);
                Assert.That(
                    (StatusCode)sre.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadIdentityTokenRejected));
                await CreateAndActivateSessionAsync(other, "Other").ConfigureAwait(false);
                Assert.That(Exists(pending.AuthenticationToken), Is.False);
            }
        }

        /// <summary>
        /// A request that is rejected for its clientNonce must not close another Session.
        /// The server ignores the nonce for SecurityPolicy None, so this calls the
        /// SessionManager.
        /// </summary>
        [Test]
        public async Task RejectedClientNonceDoesNotCloseASessionAsync()
        {
            await StartServerAsync(1).ConfigureAwait(false);
            ISessionManager manager = m_server.CurrentInstance.SessionManager;
            EndpointDescription endpoint = m_server.GetEndpoints()
                .First(e => e.SecurityPolicyUri == SecurityPolicies.None);
            var context = new OperationContext(
                new RequestHeader(),
                new SecureChannelContext("channel", endpoint, RequestEncoding.Binary),
                RequestType.CreateSession);
            byte[] nonce = Nonce.CreateRandomNonceData(32);

            CreateSessionResult first = await CreateAsync().ConfigureAwait(false);
            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await CreateAsync().ConfigureAwait(false));

            Assert.That((StatusCode)sre.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNonceInvalid));
            Assert.That(Exists(first.AuthenticationToken), Is.True);

            ValueTask<CreateSessionResult> CreateAsync()
            {
                return manager.CreateSessionAsync(
                    context, null, "Session", nonce, new ApplicationDescription(), null, null, null, kSessionTimeout, 0);
            }
        }

        /// <summary>
        /// The closed Session is counted once and reported as closed by the server
        /// (OPC 10000-4 6.5.6: SourceName "Session/Terminated").
        /// </summary>
        [Test]
        public async Task ClosedSessionIsCountedAndReportedAsync()
        {
            await StartServerAsync(2).ConfigureAwait(false);
            ServerDiagnosticsSummaryDataType diagnostics = m_server.CurrentInstance.ServerDiagnostics;
            var closing = new List<NodeId>();
            m_server.CurrentInstance.SessionManager.SessionClosing += (session, _) => closing.Add(session.Id);

            var terminated = new List<NodeId>();
            NodeStateReportEventHandler reportEvent = m_server.CurrentInstance.ServerObject.OnReportEvent;
            m_server.CurrentInstance.ServerObject.OnReportEvent = (context, node, e) =>
            {
                if (e is AuditSessionEventState audit && audit.SourceName?.Value == "Session/Terminated")
                {
                    terminated.Add(audit.SessionId?.Value);
                }
                reportEvent?.Invoke(context, node, e);
            };

            SessionClient client = await OpenChannelAsync().ConfigureAwait(false);
            CreateSessionResponse oldest = await CreateSessionAsync(client, "Oldest").ConfigureAwait(false);
            await Task.Delay(50).ConfigureAwait(false);
            await CreateSessionAsync(client, "Newer").ConfigureAwait(false);
            Assert.That(diagnostics.SessionAbortCount, Is.Zero);

            await CreateAndActivateSessionAsync(client, "Newest").ConfigureAwait(false);

            Assert.That(closing, Is.EqualTo(new[] { oldest.SessionId }));
            Assert.That(terminated, Is.EqualTo(new[] { oldest.SessionId }));
            Assert.That(diagnostics.SessionAbortCount, Is.EqualTo(1));
            Assert.That(diagnostics.CurrentSessionCount, Is.EqualTo(2));
            Assert.That(diagnostics.CumulatedSessionCount, Is.EqualTo(3));
            Assert.That(diagnostics.SessionTimeoutCount, Is.Zero);
            Assert.That(diagnostics.RejectedSessionCount, Is.Zero);
        }

        /// <summary>
        /// Clients that create Sessions at the same time never exceed the limit, are never
        /// refused while Sessions that are not activated remain, and leave an activated
        /// Session alone.
        /// </summary>
        [Test]
        public async Task ConcurrentCreateSessionKeepsTheLimitAsync()
        {
            const int maxSessionCount = 5;
            await StartServerAsync(maxSessionCount).ConfigureAwait(false);

            SessionClient first = await OpenChannelAsync().ConfigureAwait(false);
            NodeId activated = await CreateAndActivateSessionAsync(first, "Activated").ConfigureAwait(false);

            var clients = new SessionClient[4];
            for (int ii = 0; ii < clients.Length; ii++)
            {
                clients[ii] = await OpenChannelAsync().ConfigureAwait(false);
            }
            int highest = 0;
            m_server.CurrentInstance.SessionManager.SessionCreated +=
                (_, _) => highest = Math.Max(highest, SessionCount);

            await Task.WhenAll(clients.Select(async client =>
            {
                for (int ii = 0; ii < 25; ii++)
                {
                    await CreateSessionAsync(client, $"Session{ii}").ConfigureAwait(false);
                }
            })).ConfigureAwait(false);

            Assert.That(highest, Is.EqualTo(maxSessionCount));
            Assert.That(SessionCount, Is.EqualTo(maxSessionCount));
            Assert.That(Exists(activated), Is.True);
            Assert.That(
                m_server.CurrentInstance.ServerDiagnostics.CurrentSessionCount,
                Is.EqualTo(maxSessionCount));
        }

        private int SessionCount => m_server.CurrentInstance.SessionManager.GetSessions().Count;

        private bool Exists(NodeId authenticationToken)
        {
            return m_server.CurrentInstance.SessionManager.GetSession(authenticationToken) != null;
        }

        private async Task StartServerAsync(int maxSessionCount)
        {
            m_fixture = new ServerFixture<StandardServer> { SecurityNone = true };
            await m_fixture.LoadConfigurationAsync().ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration.MaxSessionCount = maxSessionCount;
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a new secure channel with SecurityPolicy None.
        /// </summary>
        private async Task<SessionClient> OpenChannelAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 30000;

            DiscoveryClient channel = await DiscoveryClient.CreateAsync(
                new Uri($"opc.tcp://localhost:{m_fixture.Port}/{nameof(StandardServer)}"),
                endpointConfiguration,
                m_telemetry).ConfigureAwait(false);

            var client = new SessionClient(channel.TransportChannel, m_telemetry);
            m_clients.Add(channel);
            m_clients.Add(client);
            return client;
        }

        private static async Task<CreateSessionResponse> CreateSessionAsync(
            SessionClient client,
            string sessionName)
        {
            CreateSessionResponse response = await client.CreateSessionAsync(
                new RequestHeader(),
                new ApplicationDescription
                {
                    ApplicationUri = "urn:localhost:UA:" + nameof(SessionCapNonActivatedTests),
                    ApplicationType = ApplicationType.Client
                },
                null,
                null,
                sessionName,
                Nonce.CreateRandomNonceData(32),
                null,
                kSessionTimeout,
                0,
                default).ConfigureAwait(false);
            return response;
        }

        private static async Task<NodeId> CreateAndActivateSessionAsync(
            SessionClient client,
            string sessionName)
        {
            CreateSessionResponse response =
                await CreateSessionAsync(client, sessionName).ConfigureAwait(false);

            await ActivateSessionAsync(client, response.AuthenticationToken).ConfigureAwait(false);

            return response.AuthenticationToken;
        }

        private static Task ActivateSessionAsync(SessionClient client, NodeId authenticationToken)
        {
            return client.ActivateSessionAsync(
                new RequestHeader { AuthenticationToken = authenticationToken },
                new SignatureData(),
                [],
                [],
                default,
                new SignatureData(),
                default);
        }
    }
}
