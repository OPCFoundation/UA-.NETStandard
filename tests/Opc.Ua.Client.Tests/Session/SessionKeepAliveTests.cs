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
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

#pragma warning disable CA2000, CA2007

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Tests for the keep alive liveness bookkeeping of <see cref="Session"/>.
    /// </summary>
    /// <remarks>
    /// Regression coverage for
    /// https://github.com/OPCFoundation/UA-.NETStandard/issues/4317:
    /// <see cref="Session.RequestCompleted"/> defers the keep alive read on every
    /// good service response, so under steady request traffic the read never
    /// fires. The freshness timestamp <see cref="Session.KeepAliveStopped"/> is
    /// judged against therefore has to be refreshed by those responses as well,
    /// otherwise the first pause in traffic reports BadNoCommunication on a
    /// perfectly healthy session.
    /// </remarks>
    [TestFixture]
    [Parallelizable]
    [Category("Client")]
    [Category("Session")]
    [Category("KeepAlive")]
    public sealed class SessionKeepAliveTests
    {
        private const int kKeepAliveInterval = 5_000;

        /// <summary>
        /// KeepAliveInterval * m_keepAliveIntervalFactor (1) + m_keepAliveGuardBand (1000).
        /// </summary>
        private static readonly TimeSpan s_pastKeepAliveThreshold =
            TimeSpan.FromMilliseconds(kKeepAliveInterval + 1_000 + 1);

        [Test]
        public void KeepAliveStoppedTripsWhenNothingIsHeardFromTheServer()
        {
            var timeProvider = new FakeTimeProvider();
            using KeepAliveTestSession session = CreateSession(timeProvider);

            Assert.That(session.KeepAliveStopped, Is.False);

            timeProvider.Advance(s_pastKeepAliveThreshold);

            Assert.That(session.KeepAliveStopped, Is.True);
        }

        /// <summary>
        /// Steady traffic keeps the session alive even though the keep alive read
        /// is deferred by every response and consequently never fires.
        /// </summary>
        [Test]
        public void GoodResponsesKeepTheSessionAliveWhileTheKeepAliveReadIsDeferred()
        {
            var timeProvider = new FakeTimeProvider();
            using KeepAliveTestSession session = CreateSession(timeProvider);

            long initialTimestamp = session.LastKeepAliveTimestamp;

            // a request every 2 s for 20 s - each response defers the keep alive
            // read by a full interval, so no keep alive read is ever sent.
            for (int ii = 0; ii < 10; ii++)
            {
                timeProvider.Advance(TimeSpan.FromSeconds(2));
                session.CompleteRequest(StatusCodes.Good);

                Assert.That(
                    session.KeepAliveStopped,
                    Is.False,
                    $"the server answered 2000 ms ago (iteration {ii})");
            }

            Assert.Multiple(() =>
            {
                Assert.That(session.KeepAliveStopped, Is.False);
                Assert.That(session.LastKeepAliveTimestamp, Is.GreaterThan(initialTimestamp));
            });

            // once the traffic stops the session must still detect the silence.
            timeProvider.Advance(s_pastKeepAliveThreshold);

            Assert.That(session.KeepAliveStopped, Is.True);
        }

        /// <summary>
        /// A good response also revives a timestamp that already went stale but
        /// has not been reported as an error yet - the keep alive read is still
        /// in flight, the worker is skipping while reconnecting, or its tick is
        /// late. Without this no traffic could rescue the session from the
        /// spurious BadNoCommunication the worker is about to raise.
        /// </summary>
        [Test]
        public void GoodResponseRevivesAStaleTimestampWhenNoErrorIsLatched()
        {
            var timeProvider = new FakeTimeProvider();
            using KeepAliveTestSession session = CreateSession(timeProvider);

            timeProvider.Advance(s_pastKeepAliveThreshold);
            Assert.That(session.KeepAliveStopped, Is.True);

            session.CompleteRequest(StatusCodes.Good);

            Assert.That(
                session.KeepAliveStopped,
                Is.False,
                "a good response must count as proof of liveness");
        }

        [Test]
        public void BadResponsesDoNotRefreshTheKeepAliveTimestamp()
        {
            var timeProvider = new FakeTimeProvider();
            using KeepAliveTestSession session = CreateSession(timeProvider);

            long initialTimestamp = session.LastKeepAliveTimestamp;

            timeProvider.Advance(TimeSpan.FromMilliseconds(3_000));
            session.CompleteRequest(StatusCodes.BadTimeout);

            Assert.That(session.LastKeepAliveTimestamp, Is.EqualTo(initialTimestamp));

            timeProvider.Advance(s_pastKeepAliveThreshold - TimeSpan.FromMilliseconds(3_000));

            Assert.That(session.KeepAliveStopped, Is.True);
        }

        /// <summary>
        /// A latched keep alive error is only cleared by an actual keep alive
        /// response, never by unrelated traffic.
        /// </summary>
        [Test]
        public void LatchedKeepAliveErrorIsNotClearedByAGoodResponse()
        {
            var timeProvider = new FakeTimeProvider();
            using KeepAliveTestSession session = CreateSession(timeProvider);

            session.RaiseKeepAliveError(StatusCodes.BadTimeout);
            Assert.That(session.KeepAliveStopped, Is.True);

            session.CompleteRequest(StatusCodes.Good);

            Assert.That(session.KeepAliveStopped, Is.True);
        }

        /// <summary>
        /// After BadNoCommunication was reported the recovery branch of
        /// OnKeepAlive must still run, i.e. good responses arriving before the
        /// keep alive read completes must not silently mark the session as
        /// healthy again.
        /// </summary>
        [Test]
        public void RecoveryAfterBadNoCommunicationIsHandledByTheKeepAliveResponse()
        {
            var timeProvider = new FakeTimeProvider();
            using KeepAliveTestSession session = CreateSession(timeProvider);

            timeProvider.Advance(s_pastKeepAliveThreshold);
            session.RaiseKeepAliveError(StatusCodes.BadNoCommunication);

            // the keep alive read itself completes as a good response first.
            session.CompleteRequest(StatusCodes.Good);
            Assert.That(
                session.KeepAliveStopped,
                Is.True,
                "the keep alive error must survive until OnKeepAlive handled it");

            session.RaiseKeepAlive();

            Assert.Multiple(() =>
            {
                Assert.That(
                    session.KeepAliveStoppedOnEnteringOnKeepAlive,
                    Is.True,
                    "OnKeepAlive must take the recovery branch");
                Assert.That(session.KeepAliveStopped, Is.False);
            });
        }

        private static KeepAliveTestSession CreateSession(TimeProvider timeProvider)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ApplicationConfiguration configuration = CreateClientConfiguration(telemetry);
            IServiceMessageContext messageContext = configuration.CreateMessageContext();
            var channel = new Mock<ITransportChannel>();
            channel.SetupGet(c => c.MessageContext).Returns(messageContext);

            var session = new KeepAliveTestSession(
                channel.Object,
                configuration,
                CreateEndpoint(),
                timeProvider)
            {
                KeepAliveInterval = kKeepAliveInterval
            };

            // establish the initial liveness baseline the keep alive timer start
            // would normally provide, then forget what that call observed.
            session.RaiseKeepAlive();
            session.ResetKeepAliveObservation();

            return session;
        }

        private static ApplicationConfiguration CreateClientConfiguration(
            ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "SessionKeepAliveTests",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = "urn:localhost:SessionKeepAliveTests",
                ProductUri = "urn:localhost:SessionKeepAliveTests",
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 60000,
                    MinSubscriptionLifetime = 10000
                },
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier()
                },
                TransportQuotas = new TransportQuotas { OperationTimeout = 6000 }
            };
        }

        private static ConfiguredEndpoint CreateEndpoint()
        {
            var description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport,
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
            description.Server.ApplicationUri = description.EndpointUrl;
            description.Server.ApplicationType = ApplicationType.Server;

            return new ConfiguredEndpoint(
                null,
                description,
                new EndpointConfiguration { OperationTimeout = 6000 })
            {
                UpdateBeforeConnect = false
            };
        }

        /// <summary>
        /// Exposes the protected keep alive hooks so the handling of service
        /// responses can be driven without a server.
        /// </summary>
        private sealed class KeepAliveTestSession : Session
        {
            public KeepAliveTestSession(
                ITransportChannel channel,
                ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint,
                TimeProvider timeProvider)
                : base(channel, configuration, endpoint, timeProvider: timeProvider)
            {
            }

            /// <summary>
            /// The value of <see cref="Session.KeepAliveStopped"/> observed when
            /// <see cref="OnKeepAlive"/> was entered the last time.
            /// </summary>
            public bool? KeepAliveStoppedOnEnteringOnKeepAlive { get; private set; }

            /// <summary>
            /// Discards what <see cref="OnKeepAlive"/> observed so far.
            /// </summary>
            public void ResetKeepAliveObservation()
            {
                KeepAliveStoppedOnEnteringOnKeepAlive = null;
            }

            /// <summary>
            /// Simulates a completed service call with the given service result.
            /// </summary>
            public void CompleteRequest(StatusCode serviceResult)
            {
                RequestCompleted(
                    new ReadRequest { RequestHeader = new RequestHeader() },
                    new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader { ServiceResult = serviceResult }
                    },
                    nameof(Read));
            }

            /// <summary>
            /// Simulates a successful keep alive read response.
            /// </summary>
            public void RaiseKeepAlive()
            {
                OnKeepAlive(ServerState.Running, DateTime.UtcNow);
            }

            /// <summary>
            /// Simulates a failed keep alive read.
            /// </summary>
            public void RaiseKeepAliveError(StatusCode statusCode)
            {
                OnKeepAliveError(new ServiceResult(statusCode));
            }

            protected override void OnKeepAlive(ServerState currentState, DateTime currentTime)
            {
                KeepAliveStoppedOnEnteringOnKeepAlive = KeepAliveStopped;
                base.OnKeepAlive(currentState, currentTime);
            }
        }
    }
}
