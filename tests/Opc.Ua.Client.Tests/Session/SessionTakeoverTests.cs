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
 *
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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Covers the session takeover capability of the raw session.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class SessionTakeoverTests
    {
        /// <summary>
        /// The generation increments once per newly bound server session, not for the same
        /// session, not when the binding is cleared, and again when an earlier id returns.
        /// </summary>
        [Test]
        public void SessionGenerationIncrementsOnlyForANewServerSession()
        {
            using TakeoverSession session = CreateSession();
            var changes = new List<(long Generation, NodeId Previous, NodeId Current)>();
            session.SessionGenerationChanged +=
                (_, args) => changes.Add((args.Generation, args.PreviousSessionId, args.SessionId));
            var first = new NodeId(1u);
            var second = new NodeId(2u);

            session.SessionCreated(first, new NodeId(11u));
            session.SessionCreated(first, new NodeId(11u));
            session.SessionCreated(default, default);
            session.SessionCreated(second, new NodeId(12u));
            session.SessionCreated(first, new NodeId(13u));

            Assert.That(session.SessionGeneration, Is.EqualTo(3));
            Assert.That(changes, Is.EqualTo(new (long, NodeId, NodeId)[]
            {
                (1, NodeId.Null, first),
                (2, first, second),
                (3, second, first)
            }));
        }

        /// <summary>
        /// A session cloned for recreation continues the generation and, when requested,
        /// the handlers of its template.
        /// </summary>
        [Test]
        public void CloneContinuesTheGeneration()
        {
            using TakeoverSession session = CreateSession();
            int calls = 0;
            session.SessionGenerationChanged += (_, _) => calls++;
            session.SessionCreated(new NodeId(1u), new NodeId(11u));
            using Session clone = session.CloneSession(session.TransportChannel, true);

            Assert.That(clone.SessionGeneration, Is.EqualTo(1));
            clone.SessionCreated(new NodeId(1u), new NodeId(11u));
            Assert.That(clone.SessionGeneration, Is.EqualTo(1), "The same server session is not a new generation.");
            clone.SessionCreated(new NodeId(2u), new NodeId(12u));

            Assert.That(clone.SessionGeneration, Is.EqualTo(2));
            Assert.That(calls, Is.EqualTo(2));
        }

        /// <summary>
        /// Retaining the server session for another owner fences every request, including
        /// CreateSession and ActivateSession, which a closed session still allows.
        /// </summary>
        [Test]
        public void RetainedServerSessionFencesEveryRequest()
        {
            using TakeoverSession session = CreateSession();
            session.SessionCreated(new NodeId(1u), new NodeId(11u));
            Assert.That(() => session.PrepareRequest(new CreateSessionRequest()), Throws.Nothing);

            session.AbandonServerSession();

            foreach (IServiceRequest request in new IServiceRequest[]
            {
                new CreateSessionRequest(),
                new ActivateSessionRequest(),
                new CloseSessionRequest(),
                new DeleteSubscriptionsRequest(),
                new ReadRequest()
            })
            {
                ServiceResultException? exception = Assert.Throws<ServiceResultException>(
                    () => session.PrepareRequest(request));
                Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed), request.GetType().Name);
            }
        }

        /// <summary>
        /// Retaining a connected session returns its server session and fences it: a later close
        /// releases it locally without sending CloseSession.
        /// </summary>
        [Test]
        public async Task RetainReturnsTheBoundSessionAndSuppressesTheCloseAsync()
        {
            var requests = new List<IServiceRequest>();
            using TakeoverSession session = CreateSession(requests);
            session.SessionCreated(new NodeId(1u), new NodeId(11u));
            session.MarkServerAlive();

            SessionConfiguration saved = await session.RetainServerSessionAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(saved.SessionId, Is.EqualTo(new NodeId(1u)));
                Assert.That(saved.AuthenticationToken, Is.EqualTo(new NodeId(11u)));
                Assert.That(saved.ConfiguredEndpoint, Is.Not.SameAs(session.ConfiguredEndpoint));
                Assert.That(session.IsServerSessionRetained, Is.True);
                Assert.That(session.SessionGeneration, Is.EqualTo(1), "Retaining is not a new generation.");
            });
            await session.CloseAsync(1000, closeChannel: false).ConfigureAwait(false);
            Assert.That(requests, Has.None.InstanceOf<CloseSessionRequest>());
        }

        /// <summary>
        /// A session that already sent its close cannot retain its server session.
        /// </summary>
        [Test]
        public async Task RetainAfterCloseFailsAsync()
        {
            var requests = new List<IServiceRequest>();
            using TakeoverSession session = CreateSession(requests);
            session.SessionCreated(new NodeId(1u), new NodeId(11u));
            session.MarkServerAlive();
            await session.CloseAsync(1000, closeChannel: false).ConfigureAwait(false);
            Assert.That(requests, Has.Some.InstanceOf<CloseSessionRequest>());

            Assert.That(
                () => session.RetainServerSessionAsync(CancellationToken.None),
                Throws.InstanceOf<ServiceResultException>());
            Assert.That(session.IsServerSessionRetained, Is.False);
        }

        /// <summary>
        /// Saved state without a session id or authentication token, without an endpoint, or for
        /// another endpoint is rejected.
        /// </summary>
        /// <param name="defect">The defect of the saved state.</param>
        [TestCase("token")]
        [TestCase("sessionId")]
        [TestCase("endpoint")]
        [TestCase("url")]
        public void ValidateHandoffRejectsInvalidSavedState(string defect)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configured = new EndpointDescription("opc.tcp://localhost:4840");
            var saved = new SessionConfiguration
            {
                SessionId = defect == "sessionId" ? NodeId.Null : new NodeId(2u),
                AuthenticationToken = defect == "token" ? NodeId.Null : new NodeId(12u),
                ConfiguredEndpoint = defect switch
                {
                    "endpoint" => null,
                    "url" => new ConfiguredEndpoint(null, new EndpointDescription("opc.tcp://otherhost:4840"), null),
                    _ => new ConfiguredEndpoint(null, new EndpointDescription("opc.tcp://localhost:4840"), null)
                }
            };

            Assert.That(
                () => Session.ValidateHandoff(saved, configured, telemetry),
                Throws.InstanceOf<ServiceResultException>());
        }

        /// <summary>
        /// Valid saved state on the configured endpoint is accepted.
        /// </summary>
        [Test]
        public void ValidateHandoffAcceptsValidSavedState()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var saved = new SessionConfiguration
            {
                SessionId = new NodeId(2u),
                AuthenticationToken = new NodeId(12u),
                ConfiguredEndpoint = new ConfiguredEndpoint(null, new EndpointDescription("opc.tcp://localhost:4840"), null)
            };

            Assert.That(
                () => Session.ValidateHandoff(
                    saved, new EndpointDescription("opc.tcp://localhost:4840"), telemetry),
                Throws.Nothing);
            Assert.That(() => Session.ValidateHandoff(saved, null, telemetry), Throws.Nothing);
        }

        /// <summary>
        /// The saved server nonce of a secure session must be a valid nonce: 32 to 128 bytes
        /// that are not all zero. A non-empty nonce alone is not enough.
        /// </summary>
        /// <param name="length">The length of the nonce.</param>
        /// <param name="fill">The value of every byte of the nonce.</param>
        /// <param name="valid">Whether the nonce is accepted.</param>
        [TestCase(0, 1, false)]
        [TestCase(4, 1, false)]
        [TestCase(31, 1, false)]
        [TestCase(32, 0, false)]
        [TestCase(129, 1, false)]
        [TestCase(32, 1, true)]
        [TestCase(64, 7, true)]
        public void ValidateHandoffChecksTheSavedServerNonceOfASecureSession(int length, int fill, bool valid)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            byte[] nonce = [.. Enumerable.Repeat((byte)fill, length)];
            var saved = new SessionConfiguration
            {
                SessionId = new NodeId(2u),
                AuthenticationToken = new NodeId(12u),
                ServerNonce = ByteString.From(nonce),
                ConfiguredEndpoint = new ConfiguredEndpoint(null, new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }, null)
            };

            if (valid)
            {
                Assert.That(() => Session.ValidateHandoff(saved, null, telemetry), Throws.Nothing);
            }
            else
            {
                Assert.That(
                    () => Session.ValidateHandoff(saved, null, telemetry),
                    Throws.InstanceOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadNonceInvalid));
            }
        }

        /// <summary>
        /// Two server certificate blobs match only when both are empty or both carry the same
        /// leaf certificate; a certificate that is missing on one side only is a mismatch.
        /// </summary>
        [Test]
        public void IsSameServerCertificateRequiresBothSidesToMatch()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate issuer = CertificateBuilder.Create("CN=TakeoverIssuer")
                .SetCAConstraint()
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder.Create("CN=TakeoverLeaf")
                .SetIssuer(issuer)
                .CreateForRSA();
            using Certificate other = CertificateBuilder.Create("CN=TakeoverOther").CreateForRSA();
            ByteString leafOnly = ByteString.From(leaf.RawData);
            ByteString chain = ByteString.From([.. leaf.RawData, .. issuer.RawData]);
            ByteString otherOnly = ByteString.From(other.RawData);

            Assert.Multiple(() =>
            {
                Assert.That(Session.IsSameServerCertificate(default, default, telemetry), Is.True);
                Assert.That(Session.IsSameServerCertificate(leafOnly, default, telemetry), Is.False);
                Assert.That(Session.IsSameServerCertificate(default, leafOnly, telemetry), Is.False);
                Assert.That(Session.IsSameServerCertificate(leafOnly, leafOnly, telemetry), Is.True);
                Assert.That(Session.IsSameServerCertificate(leafOnly, chain, telemetry), Is.True);
                Assert.That(Session.IsSameServerCertificate(leafOnly, otherOnly, telemetry), Is.False);
            });
        }

        /// <summary>
        /// A configured endpoint whose server certificate differs from the saved one, including a
        /// certificate that only one of them has, rejects the handoff.
        /// </summary>
        [Test]
        public void ValidateHandoffRejectsAOneSidedServerCertificate()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate leaf = CertificateBuilder.Create("CN=TakeoverOneSided").CreateForRSA();
            var saved = new SessionConfiguration
            {
                SessionId = new NodeId(2u),
                AuthenticationToken = new NodeId(12u),
                ConfiguredEndpoint = new ConfiguredEndpoint(null, new EndpointDescription("opc.tcp://localhost:4840"), null)
            };
            var configured = new EndpointDescription("opc.tcp://localhost:4840")
            {
                ServerCertificate = ByteString.From(leaf.RawData)
            };

            Assert.That(
                () => Session.ValidateHandoff(saved, configured, telemetry),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        /// <summary>
        /// A CreateSession response that arrives after the server session was retained for
        /// another owner is not the retained session: it is closed past the fence instead of
        /// leaking, and the open fails.
        /// </summary>
        [Test]
        public async Task LateCreatedSessionIsClosedAfterRetentionAsync()
        {
            var endpoint = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                EndpointUrl = "opc.tcp://localhost:4840",
                UserIdentityTokens = [new UserTokenPolicy()]
            };
            using var session = SessionMock.Create(endpoint);
            var lateToken = NodeId.Parse("s=late");
            var created = new TaskCompletionSource<IServiceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var closes = new List<CloseSessionRequest>();
            session.Channel
                .Setup(c => c.SendRequestAsync(It.IsAny<CreateSessionRequest>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    sent.TrySetResult(true);
                    return new ValueTask<IServiceResponse>(created.Task);
                });
            session.Channel
                .Setup(c => c.SendRequestAsync(It.IsAny<CloseSessionRequest>(), It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>((request, _) =>
                {
                    lock (closes)
                    {
                        closes.Add((CloseSessionRequest)request);
                    }
                    return new ValueTask<IServiceResponse>(new CloseSessionResponse
                    {
                        ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                    });
                });
            session.Channel.Setup(c => c.CloseAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));

#pragma warning disable CA2025 // The open task is awaited below, before the session is disposed.
            Task open = session.OpenAsync("late", new UserIdentity(), default);
#pragma warning restore CA2025
            await sent.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            session.AbandonServerSession();
            created.SetResult(new CreateSessionResponse
            {
                ServerNonce = [1, 2, 3, 4],
                SessionId = NodeId.Parse("s=late-session"),
                AuthenticationToken = lateToken,
                ServerEndpoints = [endpoint]
            });

            ServiceResultException? failure = null;
            try
            {
                await open.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                failure = ex;
            }
            Assert.That(failure, Is.Not.Null);
            lock (closes)
            {
                Assert.That(closes.Select(c => c.RequestHeader.AuthenticationToken), Is.EqualTo(new[] { lateToken }));
            }
        }

        /// <summary>
        /// Creates an unconnected session with a mocked transport that records the requests it sends.
        /// </summary>
        private static TakeoverSession CreateSession(List<IServiceRequest>? requests = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configuration = new ApplicationConfiguration(telemetry)
            {
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration { ApplicationCertificates = [] }
            };
            var channel = new Mock<ITransportChannel>();
            channel.SetupGet(value => value.MessageContext).Returns(configuration.CreateMessageContext());
            channel.Setup(value => value.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>((request, _) =>
                {
                    requests?.Add(request);
                    return new ValueTask<IServiceResponse>(new CloseSessionResponse
                    {
                        ResponseHeader = new ResponseHeader
                        {
                            RequestHandle = request.RequestHeader.RequestHandle,
                            ServiceResult = StatusCodes.Good
                        }
                    });
                });
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription("opc.tcp://localhost:4840"),
                EndpointConfiguration.Create(configuration));
            return new TakeoverSession(channel.Object, configuration, endpoint);
        }

        /// <summary>
        /// Exposes request preparation so the fence can be observed without a server.
        /// </summary>
        private sealed class TakeoverSession(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint) : Session(channel, configuration, endpoint)
        {
            /// <summary>
            /// Prepares a request header as the session does before sending it.
            /// </summary>
            public void PrepareRequest(IServiceRequest request)
            {
                request.RequestHeader ??= new RequestHeader();
                UpdateRequestHeader(request, true);
            }

            /// <summary>
            /// Records a good response, which proves the server alive as a keep-alive does.
            /// </summary>
            public void MarkServerAlive()
            {
                RequestCompleted(
                    new ReadRequest { RequestHeader = new RequestHeader() },
                    new ReadResponse { ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good } },
                    "Read");
            }
        }
    }
}
