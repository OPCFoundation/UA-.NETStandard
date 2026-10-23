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
            ((ISessionTakeover)session).SessionGenerationChanged +=
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
        /// Retirement releases the binding without closing the server session.
        /// </summary>
        [Test]
        public async Task RetireAndPreserveReleasesTheBindingAsync()
        {
            using TakeoverSession session = CreateSession();
            session.SessionCreated(new NodeId(1u), new NodeId(11u));

            await session.RetireAndPreserveAsync().ConfigureAwait(false);

            Assert.That(session.SessionId.IsNull, Is.True);
            Assert.That(session.SessionGeneration, Is.EqualTo(1), "Releasing the binding is not a new generation.");
        }

        /// <summary>
        /// A cancelled retirement does nothing.
        /// </summary>
        [Test]
        public void CancelledRetirementDoesNotStart()
        {
            using TakeoverSession session = CreateSession();
            session.SessionCreated(new NodeId(1u), new NodeId(11u));
            using var cancellation = new System.Threading.CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(async () => await session.RetireAndPreserveAsync(cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(() => session.PrepareRequest(new ReadRequest()), Throws.Nothing);
        }

        /// <summary>
        /// A retirement after a close was sent reports that the server session was not retained; once a
        /// new server session is bound it can be retained again.
        /// </summary>
        [Test]
        public async Task RetireAfterCloseReportsTheServerSessionWasNotRetainedAsync()
        {
            var requests = new List<IServiceRequest>();
            using TakeoverSession session = CreateSession(requests);
            session.SessionCreated(new NodeId(1u), new NodeId(11u));
            session.MarkServerAlive();
            await session.CloseAsync(1000, closeChannel: false).ConfigureAwait(false);
            Assert.That(requests, Has.Some.InstanceOf<CloseSessionRequest>());

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await session.RetireAndPreserveAsync().ConfigureAwait(false));
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

            session.SessionCreated(new NodeId(2u), new NodeId(12u));
            await session.RetireAndPreserveAsync().ConfigureAwait(false);
            Assert.That(session.Disposed, Is.True);
        }

        /// <summary>
        /// Preparing a raw session closes its own server session on the kept channel, loads the saved
        /// state and keeps the server-revised timeout. Until its activation only ActivateSession passes the
        /// request fence, and a close releases it locally without CloseSession.
        /// </summary>
        [Test]
        public async Task PrepareClosesTheHelperAndFencesAllButActivationAsync()
        {
            var requests = new List<IServiceRequest>();
            using TakeoverSession session = CreateSession(requests);
            session.SessionCreated(new NodeId(1u), new NodeId(11u));
            double timeout = session.SessionTimeout;
            var saved = new SessionConfiguration
            {
                SessionId = new NodeId(2u),
                AuthenticationToken = new NodeId(12u),
                ConfiguredEndpoint = session.ConfiguredEndpoint
            };

            await session.PrepareTakeoverAsync(saved, new SessionTakeoverOptions { DeleteHelperSubscriptions = false })
                .ConfigureAwait(false);

            CloseSessionRequest close = requests.OfType<CloseSessionRequest>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(close.RequestHeader.AuthenticationToken, Is.EqualTo(new NodeId(11u)));
                Assert.That(close.DeleteSubscriptions, Is.False);
                Assert.That(session.IsPrepared, Is.True);
                Assert.That(session.SessionId, Is.EqualTo(new NodeId(2u)));
                Assert.That(session.SessionTimeout, Is.EqualTo(timeout));
                Assert.That(session.SessionGeneration, Is.EqualTo(2));
            });
            ServiceResultException? fenced = Assert.Throws<ServiceResultException>(
                () => session.PrepareRequest(new ReadRequest()));
            Assert.That(fenced!.StatusCode, Is.EqualTo(StatusCodes.BadSessionNotActivated));
            ServiceResultException? activation = Assert.Throws<ServiceResultException>(
                () => session.PrepareRequest(new ActivateSessionRequest()));
            Assert.That(activation!.StatusCode, Is.EqualTo(StatusCodes.BadSessionNotActivated),
                "Only the takeover activation itself may activate the saved session.");
            Assert.That(
                async () => await session.UpdateSessionAsync(null, default).ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSessionNotActivated));

            requests.Clear();
            session.MarkServerAlive();
            await session.CloseAsync(1000, closeChannel: false).ConfigureAwait(false);
            Assert.That(requests, Has.None.InstanceOf<CloseSessionRequest>());
            Assert.That(session.IsPrepared, Is.False);
        }

        /// <summary>
        /// Saved state without a session id or authentication token, or for another endpoint, is
        /// rejected before anything is sent or changed.
        /// </summary>
        /// <param name="defect">The defect of the saved state.</param>
        [TestCase("token")]
        [TestCase("endpoint")]
        [TestCase("url")]
        public void PrepareRejectsInvalidSavedStateBeforeAnyChange(string defect)
        {
            var requests = new List<IServiceRequest>();
            using TakeoverSession session = CreateSession(requests);
            session.SessionCreated(new NodeId(1u), new NodeId(11u));
            var saved = new SessionConfiguration
            {
                SessionId = new NodeId(2u),
                AuthenticationToken = defect == "token" ? NodeId.Null : new NodeId(12u),
                ConfiguredEndpoint = defect switch
                {
                    "endpoint" => null,
                    "url" => new ConfiguredEndpoint(
                        null, new EndpointDescription("opc.tcp://otherhost:4840"), session.ConfiguredEndpoint.Configuration),
                    _ => session.ConfiguredEndpoint
                }
            };

            Assert.That(
                async () => await session.PrepareTakeoverAsync(saved).ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>());
            Assert.That(requests, Is.Empty);
            Assert.That(session.IsPrepared, Is.False);
            Assert.That(session.SessionId, Is.EqualTo(new NodeId(1u)));
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
