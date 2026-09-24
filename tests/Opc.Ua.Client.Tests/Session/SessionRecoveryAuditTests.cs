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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Exercises session recovery admission, publishing ownership, and certificate reload exclusion.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    public sealed class SessionRecoveryAuditTests
    {
        [Test]
        public async Task RequestHandlesAreDistinctAcrossSessionsAsync()
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            await using var first = new Session(
                harness.CreateOpenedStandaloneChannel(endpoint).Channel, harness.Configuration, endpoint);
            await using var second = new Session(
                harness.CreateOpenedStandaloneChannel(endpoint).Channel, harness.Configuration, endpoint);

            Assert.That(second.NewRequestHandle(), Is.Not.EqualTo(first.NewRequestHandle()));
        }

        [Test]
        public async Task PublishAndKeepAliveUseDistinctRequestHandlesAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness();
            await harness.ConnectAsync(false).ConfigureAwait(false);
            harness.Clock.Advance(TimeSpan.Zero);
            await harness.InitialKeepAliveRead.WaitAsync(s_timeout).ConfigureAwait(false);
            await using Subscriptions.ISubscription subscription = harness.AddSubscription();
            ManagedSessionReconnectHarness.WirePublish publish = await harness.NextWirePublishAsync()
                .AsTask().WaitAsync(s_timeout).ConfigureAwait(false);
            ReadRequest keepAlive = harness.Requests.ToList().OfType<ReadRequest>().First(request =>
                request.NodesToRead.Count == 1 &&
                request.NodesToRead[0].NodeId == VariableIds.Server_ServerStatus_State);

            Assert.That(
                publish.Request.RequestHeader.RequestHandle, Is.Not.EqualTo(keepAlive.RequestHeader.RequestHandle));
        }

        [TestCaseSource(nameof(InitialConnectFailures))]
        public async Task InitialConnectRotatesOnlyForConnectivityFailuresAsync(StatusCode failure, bool rotate)
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint primary = SessionChannelHarness.CreateEndpoint();
            ConfiguredEndpoint alternate = SessionChannelHarness.CreateEndpoint("opc.tcp://alternate:4840");
            alternate.Description.Server.ApplicationUri = primary.Description.Server.ApplicationUri;
            var attempted = new List<ConfiguredEndpoint>();
            var factory = new Mock<ISessionFactory>();
            factory.SetupGet(value => value.Telemetry).Returns(harness.Telemetry);
            factory.SetupGet(value => value.SubscriptionEngineFactory)
                .Returns(DefaultSubscriptionEngineFactory.Instance);
            factory.Setup(value => value.CreateAsync(
                    harness.Configuration, It.IsAny<ConfiguredEndpoint>(), false, false,
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<IUserIdentity>(),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .Returns(async (ApplicationConfiguration _, ConfiguredEndpoint endpoint, bool _, bool _,
                    string _, uint _, IUserIdentity _, ArrayOf<string> _, CancellationToken _) =>
                {
                    attempted.Add(endpoint);
                    if (ReferenceEquals(endpoint, primary))
                    {
                        throw new ServiceResultException(failure);
                    }
                    return await harness.CreateSessionAsync(endpoint).ConfigureAwait(false);
                });
            var policy = new Mock<IReconnectPolicy>();
            policy.Setup(value => value.GetNextDelay(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((TimeSpan?)null);
            using var cancellation = new CancellationTokenSource(s_timeout);
            Task<Client.ManagedSession> connecting = Client.ManagedSession.CreateAsync(
                harness.Configuration, primary, factory.Object,
                reconnectPolicy: policy.Object,
                networkRedundancy: new NetworkRedundancyOptions { AlternateEndpoints = [alternate] },
                ct: cancellation.Token);
            if (rotate)
            {
                await using Client.ManagedSession session = await connecting.ConfigureAwait(false);
                Assert.That(attempted, Is.EqualTo([primary, alternate]));
                Assert.That(session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            }
            else
            {
                Assert.ThrowsAsync<ServiceResultException>(() => connecting);
                Assert.That(attempted, Is.EqualTo([primary]));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NetworkPathRecoveryReactivatesBeforeRecreatingAsync(bool rejectSession)
        {
            var alternateRequests = new ConcurrentQueue<Type>();
            int transports = 0;
            int activations = 0;
            await using var harness = new SessionChannelHarness(configureChannel: channel =>
            {
                int index = Interlocked.Increment(ref transports);
                channel.SupportedFeatures = TransportChannelFeatures.Reconnect;
                if (index == 1)
                {
                    channel.ReconnectHandler = _ =>
                        throw new ServiceResultException(StatusCodes.BadCommunicationError);
                }
                else
                {
                    channel.Mock.As<ISecureChannel>().Setup(value => value.OpenAsync(
                            It.Is<Uri>(uri => uri.Host == "localhost"),
                            It.IsAny<TransportChannelSettings>(), It.IsAny<CancellationToken>()))
                        .Throws(new ServiceResultException(StatusCodes.BadCommunicationError));
                    channel.RequestHandler = (request, _) =>
                    {
                        if (request is ActivateSessionRequest or CreateSessionRequest)
                        {
                            alternateRequests.Enqueue(request.GetType());
                        }
                        if (request is ActivateSessionRequest &&
                            Interlocked.Increment(ref activations) == 1 &&
                            rejectSession)
                        {
                            throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                        }
                        return new ValueTask<IServiceResponse>(channel.CreateResponse(request));
                    };
                }
            });
            ConfiguredEndpoint primary = SessionChannelHarness.CreateEndpoint();
            ConfiguredEndpoint alternate = SessionChannelHarness.CreateEndpoint("opc.tcp://alternate:4840");
            alternate.Description.Server.ApplicationUri = primary.Description.Server.ApplicationUri;
            var factory = new ChannelManagerSessionFactory(harness.Manager, harness.Telemetry);
            using var cancellation = new CancellationTokenSource(s_timeout);
            await using Client.ManagedSession session = await Client.ManagedSession.CreateAsync(
                harness.Configuration, primary, factory,
                reconnectPolicy: new ReconnectPolicy { InitialDelay = TimeSpan.Zero, MaxRetries = 2 },
                networkRedundancy: new NetworkRedundancyOptions { AlternateEndpoints = [alternate] },
                ct: cancellation.Token).ConfigureAwait(false);
            NodeId initialId = session.SessionId;
            session.StateMachine.TriggerReconnect();
            await session.StateMachine.WaitForConnectedAsync(cancellation.Token).ConfigureAwait(false);

            Assert.That(alternateRequests.ToArray(), Is.EqualTo(rejectSession
                ? new[]
                {
                    typeof(ActivateSessionRequest), typeof(CreateSessionRequest), typeof(ActivateSessionRequest)
                }
                : [typeof(ActivateSessionRequest)]));
            if (!rejectSession)
            {
                Assert.That(session.SessionId, Is.EqualTo(initialId));
            }
            Assert.That(session.TransportChannel.EndpointDescription.EndpointUrl,
                Is.EqualTo(alternate.Description.EndpointUrl));
        }

        [Test]
        public async Task SingleUseReverseManagedSessionNeverFallsBackToOutboundAsync()
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            var initial = new Mock<ITransportWaitingConnection>();
            initial.SetupGet(value => value.EndpointUrl).Returns(endpoint.EndpointUrl!);
            var factory = new ChannelManagerSessionFactory(harness.Manager, harness.Telemetry);
            using var cancellation = new CancellationTokenSource(s_timeout);
            await using Client.ManagedSession session = await Client.ManagedSession.CreateAsync(
                harness.Configuration, endpoint, factory,
                reconnectPolicy: new ReconnectPolicy { InitialDelay = TimeSpan.Zero, MaxRetries = 1 },
                connection: initial.Object,
                ct: cancellation.Token).ConfigureAwait(false);

            ServiceResult result = await session.StateMachine.ReconnectWithBudgetAsync!(
                new RetryBudget(s_timeout), cancellation.Token).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelClosed));
            Assert.That(harness.CreatedChannels, Has.Count.EqualTo(1));
            Assert.That(harness.CreatedChannels[0].ReconnectCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RestoredUserNameCredentialsAreResuppliedWithoutSerializationAsync(bool renewal)
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            endpoint.Description.UserIdentityTokens =
            [
                new UserTokenPolicy
                {
                    PolicyId = "user", TokenType = UserTokenType.UserName, SecurityPolicyUri = SecurityPolicies.None
                }
            ];
            byte[] secret = Guid.NewGuid().ToByteArray();
            ScriptedChannel original = harness.CreateOpenedStandaloneChannel(endpoint);
            await using var source = new Session(original.Channel, harness.Configuration, endpoint);
            source.Restore(new SessionConfiguration
            {
                SessionName = "snapshot",
                SessionId = new NodeId("restored", 1),
                AuthenticationToken = new NodeId("token", 1),
                Identity = new UserIdentity("snapshot-user", secret),
                UserIdentityTokenPolicy = SecurityPolicies.None
            });
            using var stream = new MemoryStream();
            source.SaveSessionConfiguration(stream);
            Assert.That(stream.ToArray().AsSpan().IndexOf(secret), Is.EqualTo(-1));
            stream.Position = 0;
            SessionConfiguration saved = SessionConfiguration.Create(stream, harness.Telemetry)!;
            ScriptedChannel restored = harness.CreateOpenedStandaloneChannel(endpoint);
            await using var target = new Session(restored.Channel, harness.Configuration, endpoint);
            var supplied = new UserIdentity("snapshot-user", secret);
            if (renewal)
            {
                target.RenewUserIdentity += (_, _) => supplied;
            }
            else
            {
                saved.Identity = supplied;
            }
            target.Restore(saved);
            await target.ReconnectAsync(null, restored.Channel, CancellationToken.None).ConfigureAwait(false);
            ScriptedChannel.RequestOperation activation = await restored
                .NextOperationAsync<ActivateSessionRequest>(CancellationToken.None).ConfigureAwait(false);
            Assert.That(((ActivateSessionRequest)activation.Request).UserIdentityToken.TryGetValue(
                out UserNameIdentityToken token), Is.True);
            Assert.That(token.UserName, Is.EqualTo("snapshot-user"));
            Assert.That(token.Password.ToArray(), Is.EqualTo(secret));
            Assert.That(target.SessionId, Is.EqualTo(source.SessionId));
        }

        [Test]
        public async Task RestoredUserNameWithoutCredentialsFailsBeforeActivationAsync()
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            endpoint.Description.UserIdentityTokens =
            [
                new UserTokenPolicy
                {
                    PolicyId = "user", TokenType = UserTokenType.UserName, SecurityPolicyUri = SecurityPolicies.None
                }
            ];
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);
            await using var session = new Session(channel.Channel, harness.Configuration, endpoint);
            session.Restore(new SessionConfiguration
            {
                SessionId = new NodeId("restored", 1),
                AuthenticationToken = new NodeId("token", 1),
                IdentityToken = new UserNameIdentityToken { UserName = "snapshot-user", PolicyId = "user" },
                UserIdentityTokenPolicy = SecurityPolicies.None
            });

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => session.ReconnectAsync(null, channel.Channel, CancellationToken.None))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            Assert.That(error.Message, Does.Contain("credentials"));
            channel.Mock.Verify(value => value.SendRequestAsync(
                It.Is<IServiceRequest>(request => request is ActivateSessionRequest),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ManagedCertificateRevalidationUsesEntireServerChainAsync()
        {
            await using var harness = new ManagedSessionReconnectHarness(TimeSpan.Zero);
            await harness.ConnectAsync(false).ConfigureAwait(false);
            using Certificate issuer = CertificateBuilder.Create("CN=RevalidationIssuer").SetCAConstraint()
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder.Create("CN=RevalidationLeaf").SetIssuer(issuer).CreateForRSA();
            harness.Session.ConfiguredEndpoint.Description.ServerCertificate =
                ByteString.From([.. leaf.RawData, .. issuer.RawData]);
            var validator = new Mock<ICertificateManager>();
            string[] validated = [];
            validator.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), It.IsAny<TrustListIdentifier>(),
                    It.IsAny<Security.Certificates.CertificateValidationOptions>(), It.IsAny<CancellationToken>()))
                .Callback<CertificateCollection, TrustListIdentifier,
                    Security.Certificates.CertificateValidationOptions, CancellationToken>(
                    (chain, _, _, _) => validated = [.. chain.Select(certificate => certificate.Thumbprint)])
                .ReturnsAsync(CertificateValidationResult.Success);
            validator.Setup(value => value.ValidateAsync(
                    It.IsAny<Certificate>(), It.IsAny<TrustListIdentifier>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CertificateValidationResult.Success);
            harness.Channels.Configuration.CertificateManager = validator.Object;

            await harness.Session.RevalidateServerCertificateAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(validated, Is.EqualTo([leaf.Thumbprint, issuer.Thumbprint]));
            validator.Verify(value => value.ValidateAsync(
                It.IsAny<Certificate>(), It.IsAny<TrustListIdentifier>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(harness.Session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
        }

        [Test]
        public async Task ManagedReverseReconnectUsesFreshConnectionWithoutSessionReentryAsync()
        {
            await using var harness = new SessionChannelHarness(configureChannel: channel =>
                channel.SupportedFeatures = TransportChannelFeatures.Reconnect);
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            var initial = new Mock<ITransportWaitingConnection>();
            initial.SetupGet(value => value.EndpointUrl).Returns(endpoint.EndpointUrl!);
            var replacement = new Mock<ITransportWaitingConnection>();
            replacement.SetupGet(value => value.EndpointUrl).Returns(endpoint.EndpointUrl!);
            var factory = new ChannelManagerSessionFactory(harness.Manager, harness.Telemetry);
            using var cancellation = new CancellationTokenSource(s_timeout);
            await using var session = (Session)await factory.CreateAsync(
                harness.Configuration, initial.Object, endpoint, false, false,
                "reverse", 60000, new UserIdentity(), default, cancellation.Token).ConfigureAwait(false);
            NodeId original = session.SessionId;

            await session.ReconnectAsync(replacement.Object, null, cancellation.Token).ConfigureAwait(false);

            Assert.That(session.SessionId, Is.EqualTo(original));
            Assert.That(session.Reconnecting, Is.False);
            Assert.That(harness.CreatedChannels, Has.Count.EqualTo(1));
            Assert.That(harness.CreatedChannels[0].CreateSessionCount, Is.EqualTo(1));
            harness.CreatedChannels[0].Mock.Verify(value =>
                value.ReconnectAsync(replacement.Object, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ManagedReverseReconnectWithoutFreshConnectionFailsBeforeTransportAsync()
        {
            await using var harness = new SessionChannelHarness(configureChannel: channel =>
                channel.SupportedFeatures = TransportChannelFeatures.Reconnect);
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            var initial = new Mock<ITransportWaitingConnection>();
            initial.SetupGet(value => value.EndpointUrl).Returns(endpoint.EndpointUrl!);
            var factory = new ChannelManagerSessionFactory(harness.Manager, harness.Telemetry);
            using var cancellation = new CancellationTokenSource(s_timeout);
            await using var session = (Session)await factory.CreateAsync(
                harness.Configuration, initial.Object, endpoint, false, false,
                "reverse", 60000, new UserIdentity(), default, cancellation.Token).ConfigureAwait(false);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => session.ReconnectAsync(null, null, cancellation.Token))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelClosed));
            harness.CreatedChannels[0].Mock.Verify(value => value.ReconnectAsync(
                It.IsAny<ITransportWaitingConnection>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(harness.CreatedChannels, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CloneRecreateUsesInjectedPolicyAtChannelConstructionAsync(bool reverse)
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint("opc.tcp://127.0.0.1:0");
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);
            var registry = new Mock<ISecurityPolicyRegistry>();
            var marker = new ServiceResultException(
                StatusCodes.BadSecurityPolicyRejected, "Injected registry reached.");
            registry.Setup(value => value.GetInfo(SecurityPolicies.None)).Throws(marker);
            await using var session = new Session(
                channel.Channel, harness.Configuration, endpoint, securityPolicies: registry.Object);
            var connection = new Mock<ITransportWaitingConnection>();
            connection.SetupGet(value => value.EndpointUrl).Returns(endpoint.EndpointUrl!);
            connection.SetupGet(value => value.Handle).Returns(Mock.Of<IUaSCByteTransport>());
            using var cancellation = new CancellationTokenSource(s_timeout);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(() => reverse
                ? session.RecreateAsync(connection.Object, cancellation.Token)
                : session.RecreateAsync(cancellation.Token))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
            registry.Verify(value => value.GetInfo(SecurityPolicies.None), Times.AtLeastOnce);
        }

        /// <summary>
        /// Cancelling a recreate waiting for admission cannot leave the subscription engine paused.
        /// </summary>
        [Test]
        public async Task CancelledRecreateAdmissionDoesNotLeavePublishingPausedAsync()
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);
            var engine = new Mock<ISubscriptionEngine>();
            var factory = new Mock<ISubscriptionEngineFactory>();
            ISubscriptionEngineContext context = null!;
            factory.Setup(value => value.Create(It.IsAny<ISubscriptionEngineContext>()))
                .Returns<ISubscriptionEngineContext>(value =>
                {
                    context = value;
                    return engine.Object;
                });
            await using var session = new Session(
                channel.Channel, harness.Configuration, endpoint, engineFactory: factory.Object);
            int pauseBalance = 0;
            engine.Setup(value => value.PausePublishing()).Callback(() => pauseBalance++);
            engine.Setup(value => value.ResumePublishing()).Callback(() => pauseBalance--);
            using var cancellation = new CancellationTokenSource();
            await context.ReconnectLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Task recreate = session.RecreateInPlaceAsync(channel: channel.Channel, ct: cancellation.Token);
                cancellation.Cancel();
                Assert.That(async () => await recreate.WaitAsync(s_timeout).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>());
                Assert.That(pauseBalance, Is.Zero, "A rejected admission must preserve the publishing state.");
            }
            finally
            {
                context.ReconnectLock.Release();
            }
        }

        /// <summary>
        /// Recreation owns admission until activation finishes, and a losing caller cannot resume its publishing.
        /// </summary>
        [Test]
        public async Task RecreateKeepsOwnershipAndRejectsConcurrentCallsAsync()
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);
            var create = new AsyncOperationGate();
            channel.RequestHandler = async (request, ct) =>
            {
                if (request is CreateSessionRequest)
                {
                    await create.WaitAsync(ct).ConfigureAwait(false);
                }
                return channel.CreateResponse(request);
            };
            var engine = new Mock<ISubscriptionEngine>();
            var factory = new Mock<ISubscriptionEngineFactory>();
            factory.Setup(value => value.Create(It.IsAny<ISubscriptionEngineContext>())).Returns(engine.Object);
            await using var session = new Session(
                channel.Channel, harness.Configuration, endpoint, engineFactory: factory.Object);
            using var cancellation = new CancellationTokenSource();
            Task owner = session.RecreateInPlaceAsync(channel: channel.Channel, ct: cancellation.Token);
            try
            {
                await create.Entered.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(session.Reconnecting, Is.True, "CreateSession must remain inside recovery admission.");
                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await session.RecreateInPlaceAsync(channel: channel.Channel, ct: cancellation.Token)
                        .WaitAsync(s_timeout).ConfigureAwait(false))!;
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                engine.Verify(value => value.PausePublishing(), Times.Once);
                engine.Verify(value => value.ResumePublishing(), Times.Never);
            }
            finally
            {
                cancellation.Cancel();
                create.Release();
                await ObserveCancelledRecoveryAsync(owner).ConfigureAwait(false);
            }
            Assert.That(session.Reconnecting, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancelledRecreateRetainsAdmissionUntilPublishUnwindAsync(bool pauseDuringCleanup)
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);
            var publishUnwind = new AsyncOperationGate { IgnoreCancellation = true };
            var resumedPublish = new AsyncOperationGate();
            var create = new AsyncOperationGate();
            int publishCount = 0;
            int sessionCount = 0;
            TaskCompletionSource<bool>? successorPublishEntered = null;
            channel.RequestHandler = async (request, ct) =>
            {
                switch (request)
                {
                    case CreateSessionRequest when Interlocked.Increment(ref sessionCount) > 1:
                        await create.WaitAsync(ct).ConfigureAwait(false);
                        break;
                    case CreateSubscriptionRequest subscription:
                        return new CreateSubscriptionResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            SubscriptionId = 1,
                            RevisedPublishingInterval = subscription.RequestedPublishingInterval,
                            RevisedLifetimeCount = subscription.RequestedLifetimeCount,
                            RevisedMaxKeepAliveCount = subscription.RequestedMaxKeepAliveCount
                        };
                    case DeleteSubscriptionsRequest delete:
                        return new DeleteSubscriptionsResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            Results = delete.SubscriptionIds.ConvertAll(_ => (StatusCode)StatusCodes.Good)
                        };
                    case TransferSubscriptionsRequest transfer:
                        return new TransferSubscriptionsResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            Results = transfer.SubscriptionIds.ConvertAll(_ => new TransferResult
                            {
                                StatusCode = StatusCodes.BadSubscriptionIdInvalid
                            })
                        };
                    case PublishRequest:
                        int attemptNumber = Interlocked.Increment(ref publishCount);
                        if (attemptNumber > 1)
                        {
                            successorPublishEntered?.TrySetResult(true);
                        }
                        AsyncOperationGate gate = attemptNumber == 1
                            ? publishUnwind
                            : resumedPublish;
                        await gate.WaitAsync(ct).ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("The publish gate must end through cancellation.");
                }
                return channel.CreateResponse(request);
            };
            var engineFactory = new ObservingSubscriptionEngineFactory(TimeProvider.System);
            await using var session = new Session(
                channel.Channel, harness.Configuration, endpoint, engineFactory: engineFactory);
            await session.OpenAsync("final-drain", new UserIdentity(), CancellationToken.None).ConfigureAwait(false);
            var manager = (Subscriptions.SubscriptionManager)engineFactory.Engine.SubscriptionManager;
            await using Subscriptions.ISubscription subscription = manager.Add(
                Mock.Of<Subscriptions.ISubscriptionNotificationHandler>(),
                OptionsFactory.Create(new Subscriptions.SubscriptionOptions
                {
                    DisableUnboundedItemMode = true,
                    PublishingEnabled = true,
                    PublishingInterval = TimeSpan.FromSeconds(1)
                }));
            using var cancellation = new CancellationTokenSource();
            using var contenderCancellation = new CancellationTokenSource();
            using var successorCancellation = new CancellationTokenSource();
            Task? owner = null;
            Task? contender = null;
            Task? successor = null;
            try
            {
                await publishUnwind.Entered.WaitAsync(s_timeout).ConfigureAwait(false);
                ObservingSubscriptionEngineFactory.PublishAttempt attempt = await engineFactory
                    .NextAttemptAsync(CancellationToken.None).AsTask().WaitAsync(s_timeout).ConfigureAwait(false);
                var publishing = (AsyncManualResetEvent)typeof(Subscriptions.SubscriptionManager)
                    .GetField("m_running", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;

                // Cancelling inside DrainAsync's abort makes its first wait cancel synchronously.
                // Recreate can return its task only after entering the real uncancellable final drain.
                using CancellationTokenRegistration registration = attempt.CancellationToken.Register(
                    cancellation.Cancel);
                owner = session.RecreateInPlaceAsync(channel: channel.Channel, ct: cancellation.Token);
                bool retainedAdmission = session.Reconnecting;
                if (pauseDuringCleanup)
                {
                    engineFactory.Engine.PausePublishing();
                }
                contender = session.RecreateInPlaceAsync(
                    channel: channel.Channel, ct: contenderCancellation.Token);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(cancellation.IsCancellationRequested, Is.True);
                    Assert.That(attempt.CancellationToken.IsCancellationRequested, Is.True);
                    Assert.That(attempt.Operation.IsCompleted, Is.False);
                    Assert.That(owner.IsCompleted, Is.False);
                    Assert.That(retainedAdmission, Is.True, "The final drain must retain recovery admission.");
                    Assert.That(contender.IsCompleted, Is.True, "A contender must reject, not join the old drain.");
                    Assert.That(publishing.IsSet, Is.False);
                    Assert.That(Volatile.Read(ref publishCount), Is.EqualTo(1));
                }
                ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await contender.WaitAsync(s_timeout).ConfigureAwait(false))!;
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

                publishUnwind.Release();
                Assert.CatchAsync<OperationCanceledException>(
                    async () => await owner.WaitAsync(s_timeout).ConfigureAwait(false));
                Assert.That(attempt.Operation.IsCanceled, Is.True);
                Assert.That(session.Reconnecting, Is.False);
                Assert.That(publishing.IsSet, Is.EqualTo(!pauseDuringCleanup));

                engineFactory.Engine.PausePublishing();
                await manager.DrainAsync(CancellationToken.None).WaitAsync(s_timeout).ConfigureAwait(false);
                int publishesBeforeSuccessor = Volatile.Read(ref publishCount);
                successorPublishEntered = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                successor = session.RecreateInPlaceAsync(
                    channel: channel.Channel, ct: successorCancellation.Token);
                await create.Entered.WaitAsync(s_timeout).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(session.Reconnecting, Is.True);
                    Assert.That(publishing.IsSet, Is.False, "The old owner cannot resume a successor's pause.");
                    Assert.That(Volatile.Read(ref publishCount), Is.EqualTo(publishesBeforeSuccessor));
                    Assert.That(successorPublishEntered.Task.IsCompleted, Is.False);
                }
                create.Release();
                await successor.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(session.Reconnecting, Is.False);
                Assert.That(publishing.IsSet, Is.False);
                engineFactory.Engine.ResumePublishing();
                await successorPublishEntered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(Volatile.Read(ref publishCount), Is.EqualTo(publishesBeforeSuccessor + 1));
                Assert.That(engineFactory.CreateCount, Is.EqualTo(1));
            }
            finally
            {
                engineFactory.Engine.PausePublishing();
                cancellation.Cancel();
                contenderCancellation.Cancel();
                successorCancellation.Cancel();
                publishUnwind.Release();
                create.Release();
                foreach (Task recovery in new[] { owner, contender, successor }.OfType<Task>())
                {
                    try
                    {
                        await ObserveCancelledRecoveryAsync(recovery).ConfigureAwait(false);
                    }
                    catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadInvalidState)
                    {
                    }
                }
                await manager.DrainAsync(CancellationToken.None).WaitAsync(s_timeout).ConfigureAwait(false);
                resumedPublish.Release();
            }
        }

        [Test]
        public async Task RecreateReleasesAdmissionWhenPublishingCleanupFailsAsync()
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);
            var engine = new Mock<ISubscriptionEngine>();
            var factory = new Mock<ISubscriptionEngineFactory>();
            factory.Setup(value => value.Create(It.IsAny<ISubscriptionEngineContext>())).Returns(engine.Object);
            var failure = new InvalidOperationException("Injected publishing cleanup failure.");
            engine.Setup(value => value.ResumePublishing()).Throws(failure);
            await using var session = new Session(
                channel.Channel, harness.Configuration, endpoint, engineFactory: factory.Object);

            InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await session.RecreateInPlaceAsync(channel: channel.Channel)
                    .WaitAsync(s_timeout).ConfigureAwait(false))!;

            Assert.That(error, Is.SameAs(failure));
            Assert.That(session.Reconnecting, Is.False, "A cleanup failure must still release recovery admission.");
            engine.Setup(value => value.ResumePublishing()).Callback(static () => { });
            await session.RecreateInPlaceAsync(channel: channel.Channel).WaitAsync(s_timeout).ConfigureAwait(false);
            Assert.That(session.Reconnecting, Is.False);
            Assert.That(session.Connected, Is.True);
            Assert.That(channel.CreateSessionCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Certificate reload cannot replace certificate material while session creation still owns recovery.
        /// </summary>
        [Test]
        public async Task CertificateReloadWaitsForSessionRecoveryAsync()
        {
            var clock = new ObservableFakeTimeProvider();
            await using var harness = new SessionChannelHarness(timeProvider: clock);
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);
            var create = new AsyncOperationGate();
            channel.RequestHandler = async (request, ct) =>
            {
                if (request is CreateSessionRequest)
                {
                    await create.WaitAsync(ct).ConfigureAwait(false);
                }
                return channel.CreateResponse(request);
            };
            await using var session = new Session(
                channel.Channel, harness.Configuration, endpoint, timeProvider: clock);
            using var cancellation = new CancellationTokenSource();
            Task owner = session.RecreateInPlaceAsync(channel: channel.Channel, ct: cancellation.Token);
            Task? reload = null;
            try
            {
                await create.Entered.WaitAsync(s_timeout).ConfigureAwait(false);
                Task waiting = clock.WaitForTimerCreatedAsync(TimeSpan.FromMilliseconds(100));
                reload = session.ReloadInstanceCertificateAsync(cancellation.Token);
                Task observed = await Task.WhenAny(reload, waiting).WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(observed, Is.SameAs(waiting), "Reload must wait for the active recovery owner.");
                Assert.That(reload.IsCompleted, Is.False);
                create.Release();
                await owner.WaitAsync(s_timeout).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromMilliseconds(100));
                await reload.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(session.Reconnecting, Is.False);
            }
            finally
            {
                cancellation.Cancel();
                create.Release();
                await ObserveCancelledRecoveryAsync(owner).ConfigureAwait(false);
                if (reload != null)
                {
                    await ObserveCancelledRecoveryAsync(reload).ConfigureAwait(false);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RecreationPreservesExistingPublishingPauseAsync(bool cancel)
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);
            var create = new AsyncOperationGate();
            channel.RequestHandler = async (request, ct) =>
            {
                if (request is CreateSessionRequest)
                {
                    await create.WaitAsync(ct).ConfigureAwait(false);
                }
                return channel.CreateResponse(request);
            };
            var engineFactory = new ObservingSubscriptionEngineFactory(TimeProvider.System);
            await using var session = new Session(
                channel.Channel, harness.Configuration, endpoint, engineFactory: engineFactory);
            engineFactory.Engine.PausePublishing();
            using var cancellation = new CancellationTokenSource();
            Task recreate = session.RecreateInPlaceAsync(channel: channel.Channel, ct: cancellation.Token);
            try
            {
                await create.Entered.WaitAsync(s_timeout).ConfigureAwait(false);
                if (cancel)
                {
                    cancellation.Cancel();
                    Assert.CatchAsync<OperationCanceledException>(
                        async () => await recreate.WaitAsync(s_timeout).ConfigureAwait(false));
                }
                else
                {
                    create.Release();
                    await recreate.WaitAsync(s_timeout).ConfigureAwait(false);
                }
                Assert.That(typeof(Subscriptions.SubscriptionManager)
                    .GetField("m_running", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(engineFactory.Engine.SubscriptionManager), Is.TypeOf<AsyncManualResetEvent>());
                var publishing = (AsyncManualResetEvent)typeof(Subscriptions.SubscriptionManager)
                    .GetField("m_running", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(engineFactory.Engine.SubscriptionManager)!;
                Assert.That(publishing.IsSet, Is.False, "Recovery must not replace the caller's publishing intent.");
                Assert.That(session.Reconnecting, Is.False);
            }
            finally
            {
                cancellation.Cancel();
                create.Release();
                await ObserveCancelledRecoveryAsync(recreate).ConfigureAwait(false);
            }
        }

        private static async Task ObserveCancelledRecoveryAsync(Task task)
        {
            try
            {
                await task.WaitAsync(s_timeout).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

        private static IEnumerable<TestCaseData> InitialConnectFailures =>
        [
            new(StatusCodes.BadCommunicationError, true),
            new(StatusCodes.BadTimeout, true),
            new(StatusCodes.BadUserAccessDenied, false),
            new(StatusCodes.BadCertificateUntrusted, false)
        ];
    }
}
