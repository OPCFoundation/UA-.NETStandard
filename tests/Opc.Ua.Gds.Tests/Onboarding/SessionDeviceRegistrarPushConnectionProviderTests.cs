/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using Ua = global::Opc.Ua;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises fresh-session ownership, actual peer pinning and namespace-stable push addresses.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class SessionDeviceRegistrarPushConnectionProviderTests
    {
        /// <summary>
        /// Creates the fixture's public channel identity.
        /// </summary>
        [OneTimeSetUp]
        public void SetUp()
        {
            m_certificate = CertificateBuilder.Create("CN=Push Connection Device")
                .SetRSAKeySize(2048).CreateForRSA();
        }

        /// <summary>
        /// Releases the fixture-owned key.
        /// </summary>
        [OneTimeTearDown]
        public void TearDown()
        {
            m_certificate.Dispose();
        }

        /// <summary>
        /// Direct construction requires all trust, transport and target-selection collaborators.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ConstructorRejectsMissingCollaborator(int missing)
        {
            Assert.Throws<ArgumentNullException>(() => new SessionDeviceRegistrarPushConnectionProvider(
                missing == 0 ? null! : Configuration(),
                missing == 1 ? null! : new Mock<ISessionFactory>().Object,
                missing == 2 ? null! :
                    static (_, _) => throw new AssertionException("Target selection must not run.")));
        }

        /// <summary>
        /// The actual peer leaf is checked even when discovery contains different certificate bytes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ActualPeerPinAndNamespaceUriDetermineReturnedConnection(bool chainBlob)
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            byte[] peer = chainBlob ?
                m_certificate.RawData.Concat(m_certificate.RawData).ToArray() : m_certificate.RawData;
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(peer);
            DeviceRegistrarPushTarget target = Target(context);
            target.Endpoint.Description.ServerCertificate = ByteString.From(new byte[] { 1, 2, 3 });
            var factory = Factory(context.InnerSession);
            var provider = Provider(factory.Object, target);

            IDeviceRegistrarPushConnection connection = await provider.ConnectAsync(
                Application(), ByteString.From(m_certificate.RawData.AsSpan())).ConfigureAwait(false);

            Assert.That(connection.Session, Is.SameAs(context.InnerSession));
            Assert.That(connection.ProvisionableDeviceId, Is.EqualTo(new NodeId("device", 1)));
            Assert.That(connection.CertificateGroupId, Is.EqualTo(new NodeId("group", 1)));
            Assert.That(connection.CertificateTypeId,
                Is.EqualTo(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType));
            Assert.That(connection.OfferedEndpoint, Is.Not.SameAs(target.Endpoint.Description));
            Assert.That(connection.OfferedEndpoint.ServerCertificate.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            factory.Verify(value => value.CreateAsync(
                It.IsAny<ApplicationConfiguration>(), It.IsAny<ConfiguredEndpoint>(), false, true,
                It.IsAny<string>(), 60000, target.UserIdentity, It.IsAny<ArrayOf<string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            await connection.DisposeAsync().ConfigureAwait(false);
            int calls = context.Requests.Count;
            await connection.DisposeAsync().ConfigureAwait(false);
            Assert.That(context.Requests, Has.Count.EqualTo(calls));
            Assert.That(context.Requests.OfType<CloseSessionRequest>(), Has.Exactly(1).Items);
        }

        /// <summary>
        /// Concurrent leases have distinct sessions, and disposing either lease closes only its own session.
        /// </summary>
        [Test]
        public async Task EveryLeaseOwnsADistinctSessionUntilDisposal()
        {
            await using var first = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            await using var second = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            first.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            second.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            second.InnerSession.SessionCreated(NodeId.Parse("s=second-session"), NodeId.Parse("s=second-token"));
            Mock<ISessionFactory> factory = Factory(first.InnerSession);
            factory.SetupSequence(value => value.CreateAsync(
                It.IsAny<ApplicationConfiguration>(), It.IsAny<ConfiguredEndpoint>(), false, true,
                It.IsAny<string>(), 60000, It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(first.InnerSession)
                .ReturnsAsync(second.InnerSession);
            var provider = Provider(factory.Object, Target(first));

            await using IDeviceRegistrarPushConnection firstLease = await provider.ConnectAsync(
                Application(), ByteString.Empty).ConfigureAwait(false);
            await using IDeviceRegistrarPushConnection secondLease = await provider.ConnectAsync(
                Application(), ByteString.Empty).ConfigureAwait(false);

            Assert.That(firstLease.Session, Is.SameAs(first.InnerSession));
            Assert.That(secondLease.Session, Is.SameAs(second.InnerSession));
            Assert.That(secondLease.Session, Is.Not.SameAs(firstLease.Session));
            Assert.That(secondLease.Session.SessionId, Is.Not.EqualTo(firstLease.Session.SessionId));
            await firstLease.DisposeAsync().ConfigureAwait(false);
            Assert.That(first.Requests.OfType<CloseSessionRequest>(), Has.Exactly(1).Items);
            Assert.That(second.Requests, Is.Empty);
            await secondLease.DisposeAsync().ConfigureAwait(false);
            await firstLease.DisposeAsync().ConfigureAwait(false);
            Assert.That(first.Requests.OfType<CloseSessionRequest>(), Has.Exactly(1).Items);
            Assert.That(second.Requests.OfType<CloseSessionRequest>(), Has.Exactly(1).Items);
        }

        /// <summary>
        /// Session close discards a transaction in the real server coordinator. If close fails, a retry uses a
        /// different session and the coordinator rejects its attempt to commit the abandoned transaction.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetryUsesFreshSessionAfterTransactionCleanup(bool closeFails)
        {
            await using var first = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            await using var second = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            first.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            second.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            second.InnerSession.SessionCreated(NodeId.Parse("s=retry-session"), NodeId.Parse("s=retry-token"));
            NodeId owner = first.InnerSession.SessionId;
            var coordinator = new PushConfigurationTransactionCoordinator(NUnitTelemetryContext.Create());
            bool committed = false;
            bool released = false;
            coordinator.Stage(owner, new PushConfigurationOperation
            {
                CommitAsync = _ =>
                {
                    committed = true;
                    return Task.CompletedTask;
                },
                DisposeStaged = () => released = true
            });
            first.Handler = (request, _) =>
            {
                Assert.That(request, Is.TypeOf<CloseSessionRequest>());
                if (!closeFails)
                {
                    coordinator.CancelForSessionClose(owner);
                }
                return new ValueTask<IServiceResponse>(new CloseSessionResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = closeFails ? StatusCodes.BadCommunicationError : StatusCodes.Good
                    }
                });
            };
            Mock<ISessionFactory> factory = Factory(first.InnerSession);
            factory.SetupSequence(value => value.CreateAsync(
                It.IsAny<ApplicationConfiguration>(), It.IsAny<ConfiguredEndpoint>(), false, true,
                It.IsAny<string>(), 60000, It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(first.InnerSession)
                .ReturnsAsync(second.InnerSession);
            var provider = Provider(factory.Object, Target(first));
            IDeviceRegistrarPushConnection lease = await provider.ConnectAsync(
                Application(), ByteString.Empty).ConfigureAwait(false);

            if (closeFails)
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await lease.DisposeAsync().ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
            }
            else
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }

            await lease.DisposeAsync().ConfigureAwait(false);
            Assert.That(coordinator.IsTransactionActive, Is.EqualTo(closeFails));
            Assert.That(released, Is.EqualTo(!closeFails));
            int abandonedRequests = first.Requests.Count;
            await using IDeviceRegistrarPushConnection retry = await provider.ConnectAsync(
                Application(), ByteString.Empty).ConfigureAwait(false);
            Assert.That(retry.Session, Is.SameAs(second.InnerSession));
            Assert.That(retry.Session, Is.Not.SameAs(lease.Session));
            ServiceResult apply = await coordinator.ApplyChangesAsync(retry.Session.SessionId).ConfigureAwait(false);
            Assert.That(apply.StatusCode, Is.EqualTo(closeFails
                ? StatusCodes.BadSessionIdInvalid : StatusCodes.BadNothingToDo));
            Assert.That(committed, Is.False);
            await retry.DisposeAsync().ConfigureAwait(false);
            Assert.That(first.Requests, Has.Count.EqualTo(abandonedRequests));
            Assert.That(first.Requests.OfType<CloseSessionRequest>(), Has.Exactly(1).Items);
            Assert.That(second.Requests.OfType<CloseSessionRequest>(), Has.Exactly(1).Items);
            coordinator.Reset();
        }

        /// <summary>
        /// An unexpected actual certificate or unresolved model address closes the owned session before returning.
        /// </summary>
        [TestCase("peer")]
        [TestCase("unknown-namespace")]
        [TestCase("indexed-namespace")]
        [TestCase("remote-server")]
        [TestCase("missing-peer")]
        [TestCase("actual-application")]
        public async Task InvalidPeerOrModelAddressReleasesOwnedSession(string scenario)
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(m_certificate.RawData);
            DeviceRegistrarPushTarget target = Target(context);
            if (scenario == "missing-peer")
            {
                context.Channel.SetupGet(channel => channel.ServerChannelCertificate).Returns(Array.Empty<byte>());
            }
            else if (scenario == "actual-application")
            {
                context.Channel.Object.EndpointDescription.Server.ApplicationUri = "urn:other-actual-peer";
            }
            target = scenario switch
            {
                "unknown-namespace" => target with
                {
                    TrustListId = ExpandedNodeId.Parse("nsu=urn:missing;s=trust")
                },
                "indexed-namespace" => target with { TrustListId = new ExpandedNodeId("trust", 1) },
                "remote-server" => target with
                {
                    TrustListId = ExpandedNodeId.Parse("svr=1;nsu=urn:request-context:original;s=trust")
                },
                _ => target
            };
            var provider = Provider(Factory(context.InnerSession).Object, target);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.ConnectAsync(Application(), scenario == "peer" ?
                    ByteString.From(new byte[] { 7, 8, 9 }) : ByteString.Empty).ConfigureAwait(false));

            StatusCode expected = scenario is "peer" or "missing-peer" ? StatusCodes.BadCertificateInvalid :
                scenario == "actual-application" ? StatusCodes.BadSecurityChecksFailed :
                scenario == "unknown-namespace" ? StatusCodes.BadNodeIdUnknown : StatusCodes.BadNodeIdInvalid;
            Assert.That(error!.StatusCode, Is.EqualTo(expected));
            Assert.That(context.Requests.OfType<CloseSessionRequest>().Count(), Is.EqualTo(1));
        }

        /// <summary>
        /// Insecure selection and application substitution are rejected before creating any authenticated session.
        /// </summary>
        [TestCase("unencrypted")]
        [TestCase("none-policy")]
        [TestCase("application")]
        public async Task InvalidEndpointDoesNotInvokeSessionFactory(string scenario)
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            DeviceRegistrarPushTarget target = Target(context);
            if (scenario == "unencrypted")
            {
                target.Endpoint.Description.SecurityMode = MessageSecurityMode.Sign;
            }
            else if (scenario == "none-policy")
            {
                target.Endpoint.Description.SecurityPolicyUri = SecurityPolicies.None;
            }
            else
            {
                target.Endpoint.Description.Server.ApplicationUri = "urn:substituted";
            }
            var factory = new Mock<ISessionFactory>(MockBehavior.Strict);
            var provider = Provider(factory.Object, target);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.ConnectAsync(Application(), ByteString.Empty).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A failed admission preserves both the close status and disposal error while attempting every cleanup.
        /// </summary>
        [Test]
        public async Task AdmissionAndAllCleanupFailuresAreRetained()
        {
            var session = new Mock<Opc.Ua.Client.ISession>(MockBehavior.Strict);
            session.Setup(value => value.CloseAsync(5000, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StatusCodes.BadCommunicationError);
            var disposal = new IOException("dispose failure");
            session.Setup(value => value.DisposeAsync()).Throws(disposal);
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            var provider = Provider(Factory(session.Object).Object, Target(context));

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await provider.ConnectAsync(Application(), ByteString.Empty).ConfigureAwait(false));

            Exception[] failures = error!.Flatten().InnerExceptions.ToArray();
            Assert.That(failures, Has.Length.EqualTo(3));
            Assert.That(failures.OfType<ServiceResultException>().Select(value => value.StatusCode),
                Is.EquivalentTo(new StatusCode[] { StatusCodes.BadNotSupported, StatusCodes.BadCommunicationError }));
            Assert.That(failures, Does.Contain(disposal));
            session.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Precancellation does not invoke target selection or session creation.
        /// </summary>
        [Test]
        public async Task CancellationPreventsTargetSelection()
        {
            var factory = new Mock<ISessionFactory>(MockBehavior.Strict);
            var provider = new SessionDeviceRegistrarPushConnectionProvider(Configuration(), factory.Object,
                static (_, _) => throw new AssertionException("Target selection must not run."));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await provider.ConnectAsync(Application(), ByteString.Empty, cancellation.Token).ConfigureAwait(false));

            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A host policy change during target selection cannot enable automatic trust for the new session.
        /// </summary>
        [Test]
        public async Task AutoAcceptanceEnabledDuringSelectionPreventsSessionCreation()
        {
            await using var context = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            var factory = new Mock<ISessionFactory>(MockBehavior.Strict);
            ApplicationConfiguration configuration = Configuration();
            var provider = new SessionDeviceRegistrarPushConnectionProvider(configuration, factory.Object, (_, _) =>
            {
                configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;
                return new ValueTask<DeviceRegistrarPushTarget>(Target(context));
            });

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.ConnectAsync(Application(), ByteString.Empty).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Fluent selection constructs the adapter without creating sessions, accepting trust or owning the factory.
        /// </summary>
        [Test]
        public void FluentRegistrationDefersConnectionsAndRejectsAutoAcceptance()
        {
            var services = new ServiceCollection();
            var factory = new Mock<ISessionFactory>(MockBehavior.Strict);
            services.AddDeviceRegistrarPushConnections(Configuration(),
                static (_, _) => throw new AssertionException("Target selection must not run."), factory.Object);
            using ServiceProvider container = services.BuildServiceProvider();
            Assert.That(container.GetRequiredService<IDeviceRegistrarPushConnectionProvider>(),
                Is.TypeOf<SessionDeviceRegistrarPushConnectionProvider>());
            ApplicationConfiguration configuration = Configuration();
            configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                new SessionDeviceRegistrarPushConnectionProvider(configuration, factory.Object,
                    static (_, _) => throw new AssertionException("Target selection must not run.")));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Configuration is resolved once from the owning container, after asynchronous host initialization.
        /// </summary>
        [Test]
        public void FluentConfigurationFactoryIsDeferredAndUsesContainerServices()
        {
            var services = new ServiceCollection();
            var factory = new Mock<ISessionFactory>(MockBehavior.Strict);
            ApplicationConfiguration configuration = Configuration();
            services.AddSingleton(configuration);
            services.AddSingleton(factory.Object);
            int calls = 0;
            services.AddDeviceRegistrarPushConnections(provider =>
            {
                calls++;
                return provider.GetRequiredService<ApplicationConfiguration>();
            }, static (_, _) => throw new AssertionException("Target selection must not run."));
            Assert.That(calls, Is.Zero);
            using ServiceProvider container = services.BuildServiceProvider();

            IDeviceRegistrarPushConnectionProvider first =
                container.GetRequiredService<IDeviceRegistrarPushConnectionProvider>();
            IDeviceRegistrarPushConnectionProvider repeated =
                container.GetRequiredService<IDeviceRegistrarPushConnectionProvider>();

            Assert.That(first, Is.TypeOf<SessionDeviceRegistrarPushConnectionProvider>());
            Assert.That(repeated, Is.SameAs(first));
            Assert.That(calls, Is.EqualTo(1));
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A host configuration error propagates from provider resolution without opening a session.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FluentConfigurationFactoryRejectsMissingOrUntrustedConfiguration(bool autoAccept)
        {
            var services = new ServiceCollection();
            var factory = new Mock<ISessionFactory>(MockBehavior.Strict);
            services.AddSingleton(factory.Object);
            ApplicationConfiguration configuration = Configuration();
            configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;
            services.AddDeviceRegistrarPushConnections(
                _ => autoAccept ? configuration : null!,
                static (_, _) => throw new AssertionException("Target selection must not run."));
            using ServiceProvider container = services.BuildServiceProvider();

            if (autoAccept)
            {
                ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                    container.GetRequiredService<IDeviceRegistrarPushConnectionProvider>());
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            }
            else
            {
                InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() =>
                    container.GetRequiredService<IDeviceRegistrarPushConnectionProvider>());
                Assert.That(error!.Message, Does.Contain("configuration factory returned null"));
            }
            factory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Supplies a configured factory that returns one owned session without opening a socket.
        /// </summary>
        private static Mock<ISessionFactory> Factory(Opc.Ua.Client.ISession session)
        {
            var factory = new Mock<ISessionFactory>(MockBehavior.Strict);
            factory.Setup(value => value.CreateAsync(
                It.IsAny<ApplicationConfiguration>(), It.IsAny<ConfiguredEndpoint>(), false, true,
                It.IsAny<string>(), 60000, It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(session);
            return factory;
        }

        /// <summary>
        /// Constructs the adapter with an explicit target rather than enabling discovery trust.
        /// </summary>
        private static SessionDeviceRegistrarPushConnectionProvider Provider(
            ISessionFactory factory, DeviceRegistrarPushTarget target)
        {
            return new SessionDeviceRegistrarPushConnectionProvider(Configuration(), factory,
                (_, _) => new ValueTask<DeviceRegistrarPushTarget>(target));
        }

        /// <summary>
        /// Creates a client configuration with no automatic trust acceptance.
        /// </summary>
        private static ApplicationConfiguration Configuration()
        {
            return new ApplicationConfiguration(NUnitTelemetryContext.Create())
            {
                ApplicationName = "Push connection tests",
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration { AutoAcceptUntrustedCertificates = false }
            };
        }

        /// <summary>
        /// Returns the application whose actual endpoint must match host selection.
        /// </summary>
        private static ApplicationDescription Application()
        {
            return new ApplicationDescription { ApplicationUri = "urn:request-context:server" };
        }

        /// <summary>
        /// Creates URI-addressed model targets independent of a prior connection's namespace indexes.
        /// </summary>
        private static DeviceRegistrarPushTarget Target(SessionRequestContextTestContext context)
        {
            return new DeviceRegistrarPushTarget(
                new ConfiguredEndpoint(
                    null,
                    CoreUtils.Clone(context.Channel.Object.EndpointDescription)!,
                    EndpointConfiguration.Create()),
                new UserIdentity(),
                ExpandedNodeId.Parse("nsu=urn:request-context:original;s=device"),
                ExpandedNodeId.Parse("nsu=urn:request-context:original;s=singleton"),
                new ExpandedNodeId(Ua.ObjectIds.ServerConfiguration),
                ExpandedNodeId.Parse("nsu=urn:request-context:original;s=group"),
                new ExpandedNodeId(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                ExpandedNodeId.Parse("nsu=urn:request-context:original;s=trust"));
        }

        /// <summary>The fixture-owned key and actual peer certificate.</summary>
        private Certificate m_certificate = null!;
    }
}
