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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using ServerSession = Opc.Ua.Server.Session;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for session lifecycle defects: expiry and closing at request
    /// admission, activation racing a close, server-initiated terminations, cancellable
    /// teardown, diagnostics privilege ordering and shutdown.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public sealed class SessionLifecycleRegressionTests
    {
        private const string kChannelId = "lc-channel";

        /// <summary>
        /// LC-1: a request arriving after the session timeout must not revive the
        /// session; it is rejected and the session is terminated as timed out.
        /// </summary>
        [Test]
        public async Task RequestOnExpiredSessionIsRejectedAndClosesTheSessionAsync()
        {
            using var harness = new Harness();
            CreateSessionResult created = await harness.CreateActivatedSessionAsync().ConfigureAwait(false);

            harness.Clock.Advance(TimeSpan.FromSeconds(2));
            Assert.That(created.Session.HasExpired, Is.True);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await harness.ValidateAsync(created.AuthenticationToken, RequestType.Read)
                    .ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(harness.Manager.GetSession(created.AuthenticationToken), Is.Null);
            Assert.That(harness.Diagnostics.SessionTimeoutCount, Is.EqualTo(1u));
        }

        /// <summary>
        /// LC-1: a request at the boundary must not refresh the contact time of a
        /// session whose timeout has elapsed.
        /// </summary>
        [Test]
        public async Task RejectedRequestDoesNotReviveAnExpiredSessionAsync()
        {
            using var harness = new Harness();
            CreateSessionResult created = await harness.CreateActivatedSessionAsync().ConfigureAwait(false);
            var session = (ServerSession)created.Session;
            var channel = new SecureChannelContext(kChannelId, harness.Endpoint, RequestEncoding.Binary);

            harness.Clock.Advance(TimeSpan.FromSeconds(2));

            Assert.That(
                () => session.ValidateRequest(
                    new RequestHeader { AuthenticationToken = created.AuthenticationToken },
                    channel,
                    RequestType.Read),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadSessionClosed));
            Assert.That(session.HasExpired, Is.True, "A rejected request must not refresh the contact time.");
        }

        /// <summary>
        /// LC-1 / LC-2: a session that is being closed admits no new requests.
        /// </summary>
        [Test]
        public async Task RequestOnClosingSessionIsRejectedAsync()
        {
            using var harness = new Harness();
            CreateSessionResult created = await harness.CreateActivatedSessionAsync().ConfigureAwait(false);
            Assert.That(((ServerSession)created.Session).MarkClosing(), Is.True);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await harness.ValidateAsync(created.AuthenticationToken, RequestType.Read)
                    .ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(harness.Diagnostics.SessionTimeoutCount, Is.Zero, "A closing session is not a timeout.");
        }

        /// <summary>
        /// LC-3: ActivateSession on a session that a timeout or termination has started
        /// closing must fail instead of returning Good for a session about to be removed.
        /// </summary>
        [Test]
        public async Task ActivateSessionOnClosingSessionFailsAsync()
        {
            using var harness = new Harness();
            CreateSessionResult created = await harness.CreateActivatedSessionAsync().ConfigureAwait(false);
            Assert.That(((ServerSession)created.Session).MarkClosing(), Is.True);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await harness.ActivateAsync(created.AuthenticationToken).ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
        }

        /// <summary>
        /// LC-7: a request before ActivateSession makes the server terminate the session
        /// through the regular close path, counted in SessionAbortCount.
        /// </summary>
        [Test]
        public async Task RequestBeforeActivationTerminatesAndCountsAnAbortAsync()
        {
            using var harness = new Harness();
            CreateSessionResult created = await harness.CreateSessionAsync().ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await harness.ValidateAsync(created.AuthenticationToken, RequestType.Read)
                    .ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionNotActivated));
            harness.Server.Verify(
                s => s.CloseSessionAsync(It.IsAny<OperationContext>(), created.SessionId, false, It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(harness.Manager.GetSession(created.AuthenticationToken), Is.Null);
            Assert.That(harness.Diagnostics.SessionAbortCount, Is.EqualTo(1u));
        }

        /// <summary>
        /// LC-8: once the session is removed its teardown is not cancellable, so the
        /// SessionDiagnostics node is always deleted.
        /// </summary>
        [Test]
        public async Task CloseSessionDeletesDiagnosticsWithAnUncancellableTokenAsync()
        {
            using var harness = new Harness();
            CreateSessionResult created = await harness.CreateActivatedSessionAsync().ConfigureAwait(false);
            using var cts = new CancellationTokenSource();

            await harness.Manager.CloseSessionAsync(created.SessionId, cts.Token).ConfigureAwait(false);

            Assert.That(harness.DeleteTokens, Has.Count.EqualTo(1));
            Assert.That(harness.DeleteTokens[0].CanBeCanceled, Is.False);
            Assert.That(harness.Diagnostics.CurrentSessionCount, Is.Zero);
        }

        /// <summary>
        /// LC-9: the stale identity is re-evaluated before the return-diagnostics
        /// privilege is decided.
        /// </summary>
        [Test]
        public async Task StaleIdentityIsReevaluatedBeforeDiagnosticsPrivilegeAsync()
        {
            using var harness = new Harness(recordOrder: true);
            CreateSessionResult created = await harness.CreateActivatedSessionAsync().ConfigureAwait(false);
            harness.Order.Clear();

            using OperationContext context = await harness.ValidateAsync(
                created.AuthenticationToken,
                RequestType.Read).ConfigureAwait(false);

            Assert.That(harness.Order, Is.EqualTo(new[] { "reevaluate", "diagnostics" }));
        }

        /// <summary>
        /// LC-10: sessions still open at shutdown are closed like any other session.
        /// </summary>
        [Test]
        public async Task ShutdownClosesRemainingSessionsAsync()
        {
            using var harness = new Harness();
            CreateSessionResult created = await harness.CreateActivatedSessionAsync().ConfigureAwait(false);
            int closing = 0;
            harness.Manager.SessionClosing += (_, _) => Interlocked.Increment(ref closing);

            await harness.Manager.ShutdownAsync().ConfigureAwait(false);

            Assert.That(harness.Manager.GetSession(created.AuthenticationToken), Is.Null);
            Assert.That(closing, Is.EqualTo(1));
            Assert.That(harness.DeleteTokens, Has.Count.EqualTo(1), "The SessionDiagnostics node is removed.");
            Assert.That(harness.Diagnostics.CurrentSessionCount, Is.Zero);
        }

        /// <summary>
        /// LC-6: requests rejected by session validation are counted in the server's
        /// RejectedRequestsCount, security rejections also in SecurityRejectedRequestsCount.
        /// </summary>
        [Test]
        public async Task ValidationRejectionsAreCountedInServerDiagnosticsAsync()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var fixture = new Opc.Ua.Server.TestFramework.ServerFixture<SessionManagerExpiryTests.ExpiringSessionServer>(
                t => new SessionManagerExpiryTests.ExpiringSessionServer(t, clock));
            SessionManagerExpiryTests.ExpiringSessionServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                    await Opc.Ua.Server.TestFramework.ServerFixtureUtils
                        .CreateAndActivateSessionAsync(server, "RejectedCount")
                        .ConfigureAwait(false);
                var serverInternal = (ServerInternalData)server.CurrentInstance;
                uint rejectedBefore = serverInternal.ServerDiagnostics.RejectedRequestsCount;
                uint securityRejectedBefore = serverInternal.ServerDiagnostics.SecurityRejectedRequestsCount;
                ArrayOf<ReadValueId> nodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = VariableIds.Server_ServerStatus_State,
                        AttributeId = Attributes.Value
                    }
                ];

                // A token replayed from a different SecureChannel.
                var otherChannel = new SecureChannelContext(
                    "other-channel",
                    secureChannelContext.EndpointDescription,
                    RequestEncoding.Binary);
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await server.ReadAsync(
                        otherChannel,
                        new RequestHeader { AuthenticationToken = requestHeader.AuthenticationToken },
                        0,
                        TimestampsToReturn.Neither,
                        nodesToRead,
                        RequestLifetime.None).ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));

                Assert.That(
                    serverInternal.ServerDiagnostics.RejectedRequestsCount,
                    Is.EqualTo(rejectedBefore + 1));
                Assert.That(
                    serverInternal.ServerDiagnostics.SecurityRejectedRequestsCount,
                    Is.EqualTo(securityRejectedBefore + 1));

                await Opc.Ua.Server.TestFramework.ServerFixtureUtils
                    .CloseSessionAsync(server, secureChannelContext, requestHeader, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A session manager over a mocked server whose clock, diagnostics and close path
        /// are observable.
        /// </summary>
        private sealed class Harness : IDisposable
        {
            public Harness(bool recordOrder = false)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                Server.Setup(s => s.Telemetry).Returns(telemetry);
                Server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
                Server.Setup(s => s.MessageContext).Returns(ServiceMessageContext.CreateEmpty(telemetry));
                Server.Setup(s => s.IdentityRegistry)
                    .Returns(new ServerIdentityRegistry(new AnonymousAuthenticator()));
                Server.Setup(s => s.UpdateServerDiagnostics(It.IsAny<Action<ServerDiagnosticsSummaryDataType>>()))
                    .Callback<Action<ServerDiagnosticsSummaryDataType>>(update =>
                    {
                        lock (Diagnostics)
                        {
                            update(Diagnostics);
                        }
                    });
                var diagnostics = new Mock<IDiagnosticsNodeManager>();
                int nextSessionId = 0;
                diagnostics.Setup(d => d.CreateSessionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(),
                        It.IsAny<SessionDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<SessionSecurityDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<NodeId>(
                        new NodeId((uint)Interlocked.Increment(ref nextSessionId), 1)));
                diagnostics.Setup(d => d.DeleteSessionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(),
                        It.IsAny<NodeId>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<ServerSystemContext, NodeId, CancellationToken>((_, _, ct) => DeleteTokens.Add(ct))
                    .Returns(default(ValueTask));
                Server.Setup(s => s.DiagnosticsNodeManager).Returns(diagnostics.Object);
                Server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(Server.Object));

                var configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MinSessionTimeout = 1_000,
                        MaxSessionTimeout = 60_000,
                        MaxSessionCount = 10,
                        MaxBrowseContinuationPoints = 10,
                        MaxHistoryContinuationPoints = 10
                    }
                };
                m_certificate = DefaultCertificateFactory.Instance
                    .CreateApplicationCertificate(
                        "urn:opcfoundation:test:session-lifecycle",
                        "SessionLifecycle",
                        "CN=SessionLifecycle",
                        ["localhost"])
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                Manager = recordOrder
                    ? new OrderRecordingSessionManager(Server.Object, configuration, Clock, Order)
                    : new SessionManager(Server.Object, configuration, Clock);
                Server.Setup(s => s.SessionManager).Returns(Manager);

                // The timeout and termination paths close through the server; route them to
                // the session manager as ServerInternalData does.
                Server.Setup(s => s.CloseSessionAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<NodeId>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<OperationContext, NodeId, bool, CancellationToken>(
                        (_, id, _, ct) => Manager.CloseSessionAsync(id, ct));

                Endpoint = new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost/lifecycle",
                    SecurityPolicyUri = SecurityPolicies.None,
                    SecurityMode = MessageSecurityMode.None,
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "anonymous",
                            TokenType = UserTokenType.Anonymous,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    ]
                };
            }

            public Mock<IServerInternal> Server { get; } = new();

            public FakeTimeProvider Clock { get; } = new();

            public SessionManager Manager { get; }

            public EndpointDescription Endpoint { get; }

            public ServerDiagnosticsSummaryDataType Diagnostics { get; } = new();

            public List<CancellationToken> DeleteTokens { get; } = [];

            public List<string> Order { get; } = [];

            public async Task<CreateSessionResult> CreateSessionAsync()
            {
                CreateSessionResult created = await Manager.CreateSessionAsync(
                    CreateContext(RequestType.CreateSession),
                    m_certificate,
                    "lifecycle-regression",
                    ByteString.From(new byte[32]),
                    new ApplicationDescription
                    {
                        ApplicationUri = "urn:opcfoundation:test:session-lifecycle"
                    },
                    Endpoint.EndpointUrl,
                    m_certificate.AddRef(),
                    [],
                    1_000,
                    64 * 1024,
                    CancellationToken.None).ConfigureAwait(false);
                // SessionManager leaves CurrentSessionCount to its caller.
                Diagnostics.CurrentSessionCount++;
                return created;
            }

            public async Task<CreateSessionResult> CreateActivatedSessionAsync()
            {
                CreateSessionResult created = await CreateSessionAsync().ConfigureAwait(false);
                await ActivateAsync(created.AuthenticationToken).ConfigureAwait(false);
                return created;
            }

            public async Task ActivateAsync(NodeId authenticationToken)
            {
                await Manager.ActivateSessionAsync(
                    CreateContext(RequestType.ActivateSession),
                    authenticationToken,
                    new SignatureData(),
                    default,
                    new SignatureData(),
                    [],
                    CancellationToken.None).ConfigureAwait(false);
            }

            public async Task<OperationContext> ValidateAsync(NodeId authenticationToken, RequestType requestType)
            {
                return await Manager.ValidateRequestAsync(
                    new RequestHeader { AuthenticationToken = authenticationToken },
                    new SecureChannelContext(kChannelId, Endpoint, RequestEncoding.Binary),
                    requestType,
                    RequestLifetime.None).ConfigureAwait(false);
            }

            public void Dispose()
            {
                Manager.Dispose();
                m_certificate.Dispose();
            }

            private OperationContext CreateContext(RequestType requestType)
            {
                return new OperationContext(
                    new RequestHeader(),
                    new SecureChannelContext(kChannelId, Endpoint, RequestEncoding.Binary),
                    requestType,
                    RequestLifetime.None);
            }

            private readonly Certificate m_certificate;
        }

        /// <summary>
        /// Records the order of identity re-evaluation and the diagnostics privilege check.
        /// </summary>
        private sealed class OrderRecordingSessionManager : SessionManager
        {
            public OrderRecordingSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                TimeProvider clock,
                List<string> order)
                : base(server, configuration, clock)
            {
                m_order = order;
            }

            protected override void ReevaluateIdentityIfStale(
                ISession session,
                SecureChannelContext secureChannelContext)
            {
                m_order.Add("reevaluate");
                base.ReevaluateIdentityIfStale(session, secureChannelContext);
            }

            protected override ISession CreateSession(
                OperationContext context,
                IServerInternal server,
                Certificate serverCertificate,
                NodeId sessionCookie,
                ByteString clientNonce,
                Nonce serverNonce,
                string sessionName,
                ApplicationDescription clientDescription,
                string endpointUrl,
                Certificate clientCertificate,
                CertificateCollection clientCertificateChain,
                double sessionTimeout,
                uint maxResponseMessageSize,
                int maxRequestAge,
                int maxContinuationPoints)
            {
                return new OrderRecordingSession(
                    context,
                    server,
                    serverCertificate,
                    sessionCookie,
                    clientNonce,
                    serverNonce,
                    sessionName,
                    clientDescription,
                    endpointUrl,
                    clientCertificate,
                    clientCertificateChain,
                    sessionTimeout,
                    m_order);
            }

            private readonly List<string> m_order;
        }

        private sealed class OrderRecordingSession : ServerSession
        {
            public OrderRecordingSession(
                OperationContext context,
                IServerInternal server,
                Certificate serverCertificate,
                NodeId authenticationToken,
                ByteString clientNonce,
                Nonce serverNonce,
                string sessionName,
                ApplicationDescription clientDescription,
                string endpointUrl,
                Certificate clientCertificate,
                CertificateCollection clientCertificateChain,
                double sessionTimeout,
                List<string> order)
                : base(
                    context,
                    server,
                    serverCertificate,
                    authenticationToken,
                    clientNonce,
                    serverNonce,
                    sessionName,
                    clientDescription,
                    endpointUrl,
                    clientCertificate,
                    clientCertificateChain,
                    sessionTimeout,
                    10,
                    10)
            {
                m_order = order;
            }

            public override void ValidateDiagnosticInfo(RequestHeader requestHeader)
            {
                m_order.Add("diagnostics");
                base.ValidateDiagnosticInfo(requestHeader);
            }

            private readonly List<string> m_order;
        }
    }
}
