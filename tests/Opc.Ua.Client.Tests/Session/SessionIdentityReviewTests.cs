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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    public sealed class SessionIdentityReviewTests : ClientTestFramework
    {
        public SessionIdentityReviewTests()
            : base(Utils.UriSchemeOpcTcp)
        {
            SingleSession = false;
        }

        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            return base.OneTimeSetUpAsync();
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UrlOnlyProviderUsesRefreshedEndpointAndSecurityContextAsync(bool managedChannel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var policies = new SecurityPolicies(Telemetry);
            await using var manager = new ClientChannelManager(
                ClientFixture.Config, Telemetry, securityPolicies: policies);
            var provider = new Mock<IClientIdentityProvider>();
            provider.SetupGet(value => value.ExpiresAt).Returns(DateTime.MaxValue);
            provider.Setup(value => value.CanSatisfyAsync(
                    It.IsAny<UserTokenPolicy>(),
                    It.IsAny<IdentitySelectionContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns((UserTokenPolicy policy, IdentitySelectionContext _, CancellationToken _) =>
                    new ValueTask<CanSatisfyResult>(policy.TokenType == UserTokenType.UserName
                        ? CanSatisfyResult.Yes
                        : CanSatisfyResult.No("UserName required.")));
            provider.Setup(value => value.GetIdentityAsync(
                    It.IsAny<UserTokenPolicy>(),
                    It.IsAny<IdentitySelectionContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns((UserTokenPolicy _, IdentitySelectionContext context, CancellationToken _) =>
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(context.OfferedPolicies.IsEmpty, Is.False);
                        Assert.That(context.EnabledSecurityPolicyUris,
                            Is.EqualTo(ClientFixture.Config.SecurityConfiguration.SupportedSecurityPolicies));
                        Assert.That(context.ClientInstanceCertificateAlgorithm, Is.EqualTo(CertificateKeyAlgorithm.RSA));
                        Assert.That(context.ClientInstanceCertificateKeySize, Is.GreaterThanOrEqualTo(2048));
                        Assert.That(context.SecurityPolicyRegistry, Is.SameAs(policies));
                    });
                    return new ValueTask<IUserIdentity>(new UserIdentity("user1", "password"u8));
                });

            int anonymousAttempts = 0;
            var rejectAnonymous = new Mock<IIdentityAugmenter>();
            rejectAnonymous.Setup(value => value.AugmentAsync(
                    It.IsAny<IUserIdentity>(),
                    It.IsAny<AuthenticationContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IUserIdentity identity, AuthenticationContext _, CancellationToken _) =>
                {
                    if (identity.TokenType == UserTokenType.Anonymous)
                    {
                        Interlocked.Increment(ref anonymousAttempts);
                        return new ValueTask<AuthenticationResult>(AuthenticationResult.Reject(
                            new ServiceResult(StatusCodes.BadIdentityTokenRejected)));
                    }
                    return new ValueTask<AuthenticationResult>(AuthenticationResult.Accept(identity));
                });

            ReferenceServer.CurrentInstance.IdentityRegistry.RegisterAugmenter(rejectAnonymous.Object);
            try
            {
                ManagedSessionBuilder builder = new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                    .UseEndpoint(ServerUrl.ToString())
                    .UseSecurityPolicies(policies)
                    .WithIdentityProvider(provider.Object)
                    .WithReconnectPolicy(options => options with { MaxRetries = 1, InitialDelay = TimeSpan.Zero });
                if (managedChannel)
                {
                    builder.WithChannelManager(manager);
                }
                await using ManagedSessionType session = await builder.ConnectAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
                Assert.That(anonymousAttempts, Is.Zero);
                DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                provider.Verify(value => value.GetIdentityAsync(
                    It.IsAny<UserTokenPolicy>(),
                    It.IsAny<IdentitySelectionContext>(),
                    It.IsAny<CancellationToken>()), Times.Once);
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                ReferenceServer.CurrentInstance.IdentityRegistry.UnregisterAugmenter(rejectAnonymous.Object);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ManagerOpenChecksCurrentDiscoverySnapshotAsync(bool staticFactory)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = ServerUrl.ToString(),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            }, EndpointConfiguration.Create(ClientFixture.Config));
            await endpoint.UpdateFromServerAsync(ClientFixture.Config, Telemetry, timeout.Token)
                .ConfigureAwait(false);
            EndpointDescription discovered = endpoint.DiscoveryEndpoints[0];
            discovered.SecurityLevel = discovered.SecurityLevel == 0 ? (byte)1 : (byte)0;
            await using var manager = new ClientChannelManager(ClientFixture.Config, Telemetry);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                using ISession session = staticFactory
                    ? await Opc.Ua.Client.Session.CreateAsync(
                        manager, ClientFixture.Config, endpoint, updateBeforeConnect: false, ct: timeout.Token)
                        .ConfigureAwait(false)
                    : await new ChannelManagerSessionFactory(manager, Telemetry).CreateAsync(
                        ClientFixture.Config, endpoint, false, false, "discovery-snapshot",
                        60000, new UserIdentity(), default, timeout.Token).ConfigureAwait(false);
            });

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(exception.Message, Does.Contain("GetEndpoints"));
        }

        [Test]
        public async Task ManualReconnectRearmsExhaustedSessionWithoutReplacingItAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using ManagedSessionType session = await new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(ServerUrl.ToString())
                .WithReconnectPolicy(options => options with
                {
                    InitialDelay = TimeSpan.Zero,
                    MaxRetries = 1,
                    JitterFactor = 0
                })
                .ConnectAsync(timeout.Token).ConfigureAwait(false);
            Session inner = session.InnerSession;
            NodeId sessionId = session.SessionId;
            ConnectionStateBudgetOperation reconnect = session.StateMachine.ReconnectWithBudgetAsync!;
            session.StateMachine.ReconnectWithBudgetAsync = (_, _) =>
                Task.FromResult(new ServiceResult(StatusCodes.BadConnectionClosed));
            session.StateMachine.FailoverWithBudgetAsync = (_, _) =>
                Task.FromResult(new ServiceResult(StatusCodes.BadNotSupported));
            var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.ConnectionStateChanged += (_, change) =>
            {
                if (change.NewState == ConnectionState.Disconnected)
                {
                    disconnected.TrySetResult(true);
                }
            };
            session.StateMachine.TriggerReconnect();
            await disconnected.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            session.StateMachine.ReconnectWithBudgetAsync = reconnect;

            await session.ReloadInstanceCertificateAsync(timeout.Token).ConfigureAwait(false);
            await session.UpdateSessionAsync(session.Identity, default, timeout.Token).ConfigureAwait(false);
            await session.ReconnectAsync(null, inner.TransportChannel, timeout.Token).ConfigureAwait(false);

            Assert.That(session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            Assert.That(session.InnerSession, Is.SameAs(inner));
            Assert.That(session.SessionId, Is.EqualTo(sessionId));
            DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [TestCaseSource(nameof(s_peerCertificateErrors))]
        public async Task PeerCertificateFailureRefreshesDiscoveryBeforeRecreateAsync(StatusCode status)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            ArrayOf<EndpointDescription> endpoints = await ClientFixture.GetEndpointsAsync(ServerUrl, timeout.Token)
                .ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await ClientFixture.GetEndpointAsync(
                ServerUrl, SecurityPolicies.Basic256Sha256, endpoints).ConfigureAwait(false);
            endpoint.UpdateBeforeConnect = false;
            var channel = new Mock<ITransportChannel>();
            channel.SetupGet(value => value.MessageContext).Returns(ClientFixture.Config.CreateMessageContext());
            channel.SetupGet(value => value.EndpointDescription).Returns(endpoint.Description);
            channel.SetupGet(value => value.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
            channel.Setup(value => value.ReconnectAsync(
                    It.IsAny<ITransportWaitingConnection>(), It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(status));
            using var inner = new SessionMock(channel, ClientFixture.Config, endpoint);
            inner.Restore(new SessionConfiguration
            {
                SessionId = NodeId.Parse("s=old-session"),
                AuthenticationToken = NodeId.Parse("s=old-token"),
                ServerNonce = ByteString.From(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()),
                UserIdentityTokenPolicy = SecurityPolicies.Basic256Sha256
            });
            var factory = new Mock<ISessionFactory>();
            factory.SetupGet(value => value.Telemetry).Returns(Telemetry);
            factory.SetupGet(value => value.SubscriptionEngineFactory)
                .Returns(DefaultSubscriptionEngineFactory.Instance);
            factory.Setup(value => value.CreateAsync(
                    ClientFixture.Config, endpoint, It.IsAny<bool>(), false,
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<IUserIdentity>(),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(inner);
            await using ManagedSessionType managed = await ManagedSessionType.CreateAsync(
                ClientFixture.Config, endpoint, factory.Object,
                reconnectPolicy: new ReconnectPolicy
                {
                    InitialDelay = TimeSpan.Zero,
                    MaxRetries = 1,
                    JitterFactor = 0
                }, ct: timeout.Token).ConfigureAwait(false);
            Assert.That(endpoint.DiscoveryEndpoints.IsEmpty, Is.True);

            await managed.ReconnectAsync(null, null, timeout.Token).ConfigureAwait(false);

            Assert.That(endpoint.DiscoveryEndpoints.IsEmpty, Is.False);
            Assert.That(managed.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
            Assert.That(managed.InnerSession, Is.SameAs(inner));
            Assert.That(managed.SessionId, Is.Not.EqualTo(NodeId.Parse("s=old-session")));
            DataValue value = await managed.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            await managed.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [Test]
        public async Task OpenUsesRefreshedDiscoveryInsteadOfConstructionSnapshotAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = ServerUrl.ToString(),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            }, EndpointConfiguration.Create(ClientFixture.Config));
            var factory = new DefaultSessionFactory(Telemetry);
            ITransportChannel channel = await factory.CreateChannelAsync(
                ClientFixture.Config, null, endpoint, true, false, timeout.Token).ConfigureAwait(false);
            using ISession session = factory.Create(
                channel, ClientFixture.Config, endpoint, availableEndpoints: [endpoint.Description]);
            await endpoint.UpdateFromServerAsync(ClientFixture.Config, Telemetry, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(endpoint.DiscoveryEndpoints.Count, Is.GreaterThan(1));

            await session.OpenAsync(
                "refreshed-snapshot", 60000, new UserIdentity(), default, false, timeout.Token)
                .ConfigureAwait(false);

            Assert.That(session.Connected, Is.True);
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SharedLeaseSurvivesRepeatedSessionRecoveryAsync(bool anonymousSign)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = ServerUrl.ToString(),
                SecurityMode = anonymousSign ? MessageSecurityMode.Sign : MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            }, EndpointConfiguration.Create(ClientFixture.Config));
            await using var manager = new ClientChannelManager(ClientFixture.Config, Telemetry);
            using Session session = await Opc.Ua.Client.Session.CreateAsync(
                manager, ClientFixture.Config, endpoint, ct: timeout.Token).ConfigureAwait(false);
            IManagedTransportChannel lease = session.ManagedChannel!;
            int readyEvents = 0;
            lease.StateChanged += (_, change) =>
            {
                if (change.NewState == ChannelState.Ready)
                {
                    Interlocked.Increment(ref readyEvents);
                }
            };
            NodeId previousId = session.SessionId;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await manager.ReconnectAsync(lease, timeout.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(session.TransportChannel, Is.SameAs(lease));
                    Assert.That(session.ManagedChannel, Is.SameAs(lease));
                    Assert.That(manager.GetChannelDiagnostics().Single().Refcount, Is.EqualTo(1));
                    Assert.That(manager.GetChannelDiagnostics().Single().ParticipantCount, Is.EqualTo(1));
                    Assert.That(lease.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(readyEvents, Is.EqualTo(attempt + 1));
                    Assert.That(session.SessionId, anonymousSign ? Is.Not.EqualTo(previousId) : Is.EqualTo(previousId));
                });
                DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                previousId = session.SessionId;
            }
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(manager.GetChannelDiagnostics(), Is.Empty);
        }

        [Test]
        public async Task RawRecreatePreservesMessageContextTablesAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = ServerUrl.ToString(),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            }, EndpointConfiguration.Create(ClientFixture.Config));
            var factory = new DefaultSessionFactory(Telemetry);
            using ISession created = await factory.CreateAsync(
                ClientFixture.Config, endpoint, true, false, "persistent-context",
                60000, new UserIdentity(), default, timeout.Token).ConfigureAwait(false);
            var session = (Session)created;
            IServiceMessageContext context = session.MessageContext;
            NamespaceTable namespaces = session.NamespaceUris;
            StringTable serverUris = session.ServerUris;
            NodeId originalSessionId = session.SessionId;

            await session.RecreateInPlaceAsync(ct: timeout.Token).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(session.SessionId, Is.Not.EqualTo(originalSessionId));
                Assert.That(session.TransportChannel.MessageContext.NamespaceUris, Is.SameAs(namespaces));
                Assert.That(session.TransportChannel.MessageContext.Factory, Is.SameAs(context.Factory));
                Assert.That(session.ServerUris, Is.SameAs(serverUris));
                Assert.That(namespaces.Count, Is.GreaterThan(1));
            });
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UserNameOnSignUsesEffectiveTokenPolicyAsync(bool explicitNone)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            ArrayOf<EndpointDescription> endpoints = await ClientFixture
                .GetEndpointsAsync(ServerUrl, timeout.Token).ConfigureAwait(false);
            EndpointDescription description = endpoints.ToList().First(endpoint =>
                endpoint.SecurityMode == MessageSecurityMode.Sign &&
                endpoint.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
            UserTokenPolicy policy = description.UserIdentityTokens.ToList().First(token =>
                token.TokenType == UserTokenType.UserName &&
                string.IsNullOrEmpty(token.SecurityPolicyUri));
            if (explicitNone)
            {
                policy.SecurityPolicyUri = SecurityPolicies.None;
            }
            var endpoint = new ConfiguredEndpoint(
                null,
                description,
                EndpointConfiguration.Create(ClientFixture.Config));
            var identity = new UserIdentity("user1", "password"u8) { PolicyId = policy.PolicyId };
            var factory = new DefaultSessionFactory(Telemetry);

            if (explicitNone)
            {
                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await factory.CreateAsync(
                        ClientFixture.Config, endpoint, false, false, "unencrypted-token",
                        60000, identity, default, timeout.Token).ConfigureAwait(false));
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                return;
            }

            using ISession session = await factory.CreateAsync(
                ClientFixture.Config, endpoint, false, false, "inherited-token",
                60000, identity, default, timeout.Token).ConfigureAwait(false);

            Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        private static readonly StatusCode[] s_peerCertificateErrors =
        [
            StatusCodes.BadCertificateInvalid,
            StatusCodes.BadCertificateUntrusted,
            StatusCodes.BadSecurityChecksFailed
        ];
    }
}
