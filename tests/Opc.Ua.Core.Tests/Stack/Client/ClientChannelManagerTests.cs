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

#nullable enable

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ClientChannelManagerTests
    {
        private static readonly ICertificateFactory s_factory = DefaultCertificateFactory.Instance;

        [Test]
        public async Task CreateChannelShouldCreateChannelWithConnectionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using Certificate serverCertificate = s_factory.CreateCertificate("CN=server").CreateForRSA();
            using Certificate clientCertificate = s_factory.CreateCertificate("CN=client").CreateForRSA();
            using var clientCertificateChain = new CertificateCollection();

            Certificate? parsedServerCert = null;
            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);
            transportChannelMock.Setup(x => x.OpenAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ITransportWaitingConnection, TransportChannelSettings, CancellationToken>(
                    (_, s, _) => parsedServerCert = s.ServerCertificate)
                .Returns(new ValueTask());

            var transportWaitingConnectionMock = new Mock<ITransportWaitingConnection>();
            var serviceMessageContextMock = new Mock<IServiceMessageContext>();
            serviceMessageContextMock.SetupGet(x => x.Telemetry).Returns(telemetry);
            var configuration = new ApplicationConfiguration(telemetry);

            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);

            ITransportChannel channel = await sut.CreateChannelAsync(
                GetTestEndpoint(serverCertificate),
                serviceMessageContextMock.Object,
                clientCertificate,
                clientCertificateChain,
                transportWaitingConnectionMock.Object).ConfigureAwait(false);

            Assert.That(channel, Is.Not.Null);
            parsedServerCert?.Dispose();
        }

        [Test]
        public async Task CreateChannelShouldCreateChannelAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate serverCertificate = s_factory.CreateCertificate("CN=server").CreateForRSA();
            using Certificate clientCertificate = s_factory.CreateCertificate("CN=client").CreateForRSA();
            using var clientCertificateChain = new CertificateCollection();

            Certificate? parsedServerCert = null;
            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);
            transportChannelMock.Setup(x => x.OpenAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Uri, TransportChannelSettings, CancellationToken>(
                    (_, s, _) => parsedServerCert = s.ServerCertificate)
                .Returns(new ValueTask());

            var serviceMessageContextMock = new Mock<IServiceMessageContext>();
            serviceMessageContextMock.SetupGet(x => x.Telemetry).Returns(telemetry);
            var configuration = new ApplicationConfiguration(telemetry);

            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);

            ITransportChannel channel = await sut.CreateChannelAsync(
                GetTestEndpoint(serverCertificate),
                serviceMessageContextMock.Object,
                clientCertificate,
                clientCertificateChain).ConfigureAwait(false);

            Assert.That(channel, Is.Not.Null);
            parsedServerCert?.Dispose();
        }

        [Test]
        public void CloseChannelShouldDisposeChannel()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);

            var configuration = new ApplicationConfiguration(telemetry);

            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);
            transportChannelMock
                    .SetupAdd(c => c.OnTokenActivated += It.IsAny<ChannelTokenActivatedEventHandler>())
                .Verifiable(Times.Once);

            sut.CloseChannel(transportChannelMock.Object);

            transportChannelMock.VerifyRemove(
                c => c.OnTokenActivated -= It.IsAny<ChannelTokenActivatedEventHandler>(), Times.Once);
            transportChannelMock.Verify(c => c.Dispose(), Times.Once);
        }

        [Test]
        public void OnChannelTokenActivatedShouldInvokeOnDiagnosticsButWithoutEndpointsForNonTcpChannels()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);
            var configuration = new ApplicationConfiguration(telemetry);

            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);
            var token = new ChannelToken
            {
                ChannelId = 123,
                TokenId = 456,
                CreatedAt = DateTime.UtcNow,
                Lifetime = 60000,
                ClientInitializationVector = [1, 2, 3],
                ClientEncryptingKey = [4, 5, 6],
                ClientSigningKey = [7, 8, 9],
                ServerInitializationVector = [10, 11, 12],
                ServerEncryptingKey = [13, 14, 15],
                ServerSigningKey = [16, 17, 18]
            };

            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            sut.OnChannelTokenActivated(transportChannelMock.Object, token, null);

            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic!.ChannelId, Is.EqualTo(token.ChannelId));
            Assert.That(diagnostic.TokenId, Is.EqualTo(token.TokenId));
            Assert.That(diagnostic.CreatedAt, Is.EqualTo(token.CreatedAt));
            Assert.That(diagnostic.Lifetime, Is.EqualTo(TimeSpan.FromMilliseconds(token.Lifetime)));
            Assert.That(diagnostic.Client, Is.Not.Null);
            Assert.That(diagnostic.Server, Is.Not.Null);
            Assert.That(diagnostic.RemoteIpAddress, Is.Null);
            Assert.That(diagnostic.RemotePort, Is.Null);
            Assert.That(diagnostic.LocalIpAddress, Is.Null);
            Assert.That(diagnostic.LocalPort, Is.Null);
        }

        [Test]
        public void OnChannelTokenActivatedShouldInvokeOnDiagnostics()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);

            var configuration = new ApplicationConfiguration(telemetry);

            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);
            var token = new ChannelToken
            {
                ChannelId = 123,
                TokenId = 456,
                CreatedAt = DateTime.UtcNow,
                Lifetime = 60000,
                ClientInitializationVector = [1, 2, 3],
                ClientEncryptingKey = [4, 5, 6],
                ClientSigningKey = [7, 8, 9],
                ServerInitializationVector = [10, 11, 12],
                ServerEncryptingKey = [13, 14, 15],
                ServerSigningKey = [16, 17, 18]
            };

            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            sut.OnChannelTokenActivated(transportChannelMock.Object, token, null);

            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic!.ChannelId, Is.EqualTo(token.ChannelId));
            Assert.That(diagnostic.TokenId, Is.EqualTo(token.TokenId));
            Assert.That(diagnostic.CreatedAt, Is.EqualTo(token.CreatedAt));
            Assert.That(diagnostic.Lifetime, Is.EqualTo(TimeSpan.FromMilliseconds(token.Lifetime)));
            Assert.That(diagnostic.Client, Is.Not.Null);
            Assert.That(diagnostic.Server, Is.Not.Null);
            // Endpoint fields are only populated when the channel is a
            // UaSCUaBinaryTransportChannel (mocks return null here).
            Assert.That(diagnostic.RemoteIpAddress, Is.Null);
            Assert.That(diagnostic.LocalIpAddress, Is.Null);
        }

        [Test]
        public void OnChannelTokenActivatedWithMissingVectorShouldInvokeOnDiagnostics()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);

            var configuration = new ApplicationConfiguration(telemetry);

            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);
            var token = new ChannelToken
            {
                ChannelId = 123,
                TokenId = 456,
                CreatedAt = DateTime.UtcNow,
                Lifetime = 60000,
                ClientInitializationVector = [],
                ClientEncryptingKey = [4, 5, 6],
                ClientSigningKey = [7, 8, 9],
                ServerInitializationVector = null,
                ServerEncryptingKey = [13, 14, 15],
                ServerSigningKey = [16, 17, 18]
            };
            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            sut.OnChannelTokenActivated(transportChannelMock.Object, token, null);

            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic!.ChannelId, Is.EqualTo(token.ChannelId));
            Assert.That(diagnostic.TokenId, Is.EqualTo(token.TokenId));
            Assert.That(diagnostic.CreatedAt, Is.EqualTo(token.CreatedAt));
            Assert.That(diagnostic.Lifetime, Is.EqualTo(TimeSpan.FromMilliseconds(token.Lifetime)));
            Assert.That(diagnostic.Client, Is.Null);
            Assert.That(diagnostic.Server, Is.Null);
        }

        [Test]
        public void OnChannelTokenActivatedWithMissingKeyShouldInvokeOnDiagnostics1()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);

            var configuration = new ApplicationConfiguration(telemetry);
            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);
            var token = new ChannelToken
            {
                ChannelId = 123,
                TokenId = 456,
                CreatedAt = DateTime.UtcNow,
                Lifetime = 60000,
                ClientInitializationVector = [4, 5, 6],
                ClientEncryptingKey = null,
                ClientSigningKey = [7, 8, 9],
                ServerInitializationVector = [13, 14, 15],
                ServerEncryptingKey = [13, 14, 15],
                ServerSigningKey = null
            };

            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannelMock.Object, token, null);

            // Assert
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic!.ChannelId, Is.EqualTo(token.ChannelId));
            Assert.That(diagnostic.TokenId, Is.EqualTo(token.TokenId));
            Assert.That(diagnostic.CreatedAt, Is.EqualTo(token.CreatedAt));
            Assert.That(diagnostic.Lifetime, Is.EqualTo(TimeSpan.FromMilliseconds(token.Lifetime)));
            Assert.That(diagnostic.Client, Is.Null);
            Assert.That(diagnostic.Server, Is.Null);
        }

        [Test]
        public void OnChannelTokenActivatedWithMissingKeyShouldInvokeOnDiagnostics2()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);

            var configuration = new ApplicationConfiguration(telemetry);
            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);
            var token = new ChannelToken
            {
                ChannelId = 123,
                TokenId = 456,
                CreatedAt = DateTime.UtcNow,
                Lifetime = 60000,
                ClientInitializationVector = [4, 5, 6],
                ClientEncryptingKey = [],
                ClientSigningKey = [7, 8, 9],
                ServerInitializationVector = [13, 14, 15],
                ServerEncryptingKey = [13, 14, 15],
                ServerSigningKey = []
            };

            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            sut.OnChannelTokenActivated(transportChannelMock.Object, token, null);

            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic!.ChannelId, Is.EqualTo(token.ChannelId));
            Assert.That(diagnostic.TokenId, Is.EqualTo(token.TokenId));
            Assert.That(diagnostic.CreatedAt, Is.EqualTo(token.CreatedAt));
            Assert.That(diagnostic.Lifetime, Is.EqualTo(TimeSpan.FromMilliseconds(token.Lifetime)));
            Assert.That(diagnostic.Client, Is.Null);
            Assert.That(diagnostic.Server, Is.Null);
        }

        [Test]
        public void OnChannelTokenActivatedOnNonSocketChannelShouldInvokeOnDiagnostics()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);

            var configuration = new ApplicationConfiguration(telemetry);
            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);
            var token = new ChannelToken
            {
                ChannelId = 123,
                TokenId = 456,
                CreatedAt = DateTime.UtcNow,
                Lifetime = 60000,
                ClientInitializationVector = [1, 2, 3],
                ClientEncryptingKey = [4, 5, 6],
                ClientSigningKey = [7, 8, 9],
                ServerInitializationVector = [10, 11, 12],
                ServerEncryptingKey = [13, 14, 15],
                ServerSigningKey = [16, 17, 18]
            };

            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            sut.OnChannelTokenActivated(transportChannelMock.Object, token, null);

            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic!.ChannelId, Is.EqualTo(token.ChannelId));
            Assert.That(diagnostic.TokenId, Is.EqualTo(token.TokenId));
            Assert.That(diagnostic.CreatedAt, Is.EqualTo(token.CreatedAt));
            Assert.That(diagnostic.Lifetime, Is.EqualTo(TimeSpan.FromMilliseconds(token.Lifetime)));
            Assert.That(diagnostic.Client, Is.Not.Null);
            Assert.That(diagnostic.Server, Is.Not.Null);
            Assert.That(diagnostic.RemoteIpAddress, Is.Null);
            Assert.That(diagnostic.RemotePort, Is.Null);
            Assert.That(diagnostic.LocalIpAddress, Is.Null);
            Assert.That(diagnostic.LocalPort, Is.Null);
        }

        [Test]
        public void OnChannelTokenActivatedShouldNotInvokeOnDiagnosticsWhenTokenIsNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);

            var configuration = new ApplicationConfiguration(telemetry);
            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);

            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            sut.OnChannelTokenActivated(transportChannelMock.Object, null, null);

            Assert.That(diagnostic, Is.Null);
        }

        [Test]
        public void OnChannelTokenActivatedShouldNotThrowWhenOnDiagnosticsIsNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);

            var configuration = new ApplicationConfiguration(telemetry);
            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);

            sut.OnChannelTokenActivated(transportChannelMock.Object, null, null);
        }

        [Test]
        public async Task ManagedGetAsyncShouldShareChannelAndReleaseByReferenceCountAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConfiguredEndpoint endpoint = GetTestEndpoint();
            var channelMock = CreateManagedChannelMock(endpoint);
            var transportBindingsMock = CreateBindings(channelMock, telemetry);
            var manager = new ClientChannelManager(new ApplicationConfiguration(telemetry), telemetry, transportBindingsMock.Object);
            var participant1 = CreateParticipant("p1", endpoint);
            var participant2 = CreateParticipant("p2", endpoint);

            IManagedTransportChannel lease1 = await manager.GetAsync(participant1.Object).ConfigureAwait(false);
            IManagedTransportChannel lease2 = await manager.GetAsync(participant2.Object).ConfigureAwait(false);

            Assert.That(lease1, Is.Not.SameAs(lease2));
            Assert.That(manager.GetChannelDiagnostics(), Has.Count.EqualTo(1));
            Assert.That(manager.GetChannelDiagnostics()[0].Refcount, Is.EqualTo(2));
            Assert.That(manager.GetChannelDiagnostics()[0].ParticipantCount, Is.EqualTo(2));

            await lease1.CloseAsync().ConfigureAwait(false);

            Assert.That(manager.GetChannelDiagnostics(), Has.Count.EqualTo(1));
            Assert.That(manager.GetChannelDiagnostics()[0].Refcount, Is.EqualTo(1));
            channelMock.Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);

            await lease2.CloseAsync().ConfigureAwait(false);

            Assert.That(manager.GetChannelDiagnostics(), Is.Empty);
            channelMock.Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
            channelMock.Verify(c => c.Dispose(), Times.Once);
        }

        [Test]
        public async Task ManagedLeaseShouldForwardPropertiesAndRequestsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConfiguredEndpoint endpoint = GetTestEndpoint();
            var channelMock = CreateManagedChannelMock(endpoint);
            var response = new ReadResponse();
            channelMock
                .Setup(c => c.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IServiceResponse>(response));
            var transportBindingsMock = CreateBindings(channelMock, telemetry);
            var manager = new ClientChannelManager(new ApplicationConfiguration(telemetry), telemetry, transportBindingsMock.Object);
            var participant = CreateParticipant("p1", endpoint);

            IManagedTransportChannel lease = await manager.GetAsync(participant.Object).ConfigureAwait(false);
            lease.OperationTimeout = 1234;
            IServiceResponse actualResponse = await lease.SendRequestAsync(new ReadRequest()).ConfigureAwait(false);

            Assert.That(lease.SupportedFeatures, Is.EqualTo(TransportChannelFeatures.Reconnect));
            Assert.That(lease.EndpointDescription.EndpointUrl, Is.EqualTo(endpoint.Description.EndpointUrl));
            Assert.That(lease.EndpointConfiguration.OperationTimeout, Is.EqualTo(endpoint.Configuration!.OperationTimeout));
            Assert.That(lease.ChannelThumbprint, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(lease.ClientChannelCertificate, Is.EqualTo(new byte[] { 4, 5, 6 }));
            Assert.That(lease.ServerChannelCertificate, Is.EqualTo(new byte[] { 7, 8, 9 }));
            Assert.That(actualResponse, Is.SameAs(response));
            channelMock.VerifySet(c => c.OperationTimeout = 1234, Times.Once);

            await lease.CloseAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ManagedReconnectShouldReconnectUnderlyingAndNotifyParticipantsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConfiguredEndpoint endpoint = GetTestEndpoint();
            var channelMock = CreateManagedChannelMock(endpoint);
            var transportBindingsMock = CreateBindings(channelMock, telemetry);
            var policy = new Mock<IChannelReconnectPolicy>();
            policy.Setup(p => p.GetDelay(It.IsAny<int>())).Returns(TimeSpan.Zero);
#if NETSTANDARD2_1 || NET8_0_OR_GREATER
            policy.Setup(p => p.GetDelay(It.IsAny<int>(), It.IsAny<IRetryBudget>())).Returns(TimeSpan.Zero);
            policy.SetupGet(p => p.ParticipantTimeout).Returns(Timeout.InfiniteTimeSpan);
#endif
            var manager = new ClientChannelManager(
                new ApplicationConfiguration(telemetry),
                telemetry,
                transportBindingsMock.Object,
                policy.Object);
            var participant = CreateParticipant("p1", endpoint);
            participant
                .Setup(p => p.OnReconnectAsync(
                    It.IsAny<IManagedTransportChannel>(),
                    0,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ParticipantReconnectResult>(ParticipantReconnectResult.Reactivated));

            IManagedTransportChannel lease = await manager.GetAsync(participant.Object).ConfigureAwait(false);

            await manager.ReconnectAsync(lease).ConfigureAwait(false);

            channelMock.Verify(c => c.ReconnectAsync(null, It.IsAny<CancellationToken>()), Times.Once);
            participant.Verify(p => p.OnReconnectAsync(lease, 0, It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(manager.GetChannelDiagnostics()[0].State, Is.EqualTo(ChannelState.Ready));

            await lease.CloseAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ManagedGetAsyncShouldEnforceMaxChannelsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConfiguredEndpoint endpoint1 = GetTestEndpoint("opc.tcp://localhost:4840");
            ConfiguredEndpoint endpoint2 = GetTestEndpoint("opc.tcp://localhost:4841");
            var channelMock = CreateManagedChannelMock(endpoint1);
            var transportBindingsMock = CreateBindings(channelMock, telemetry);
            var manager = new ClientChannelManager(
                new ApplicationConfiguration(telemetry),
                telemetry,
                transportBindingsMock.Object,
                options: new ChannelManagerOptions { MaxChannels = 1 });

            IManagedTransportChannel lease = await manager.GetAsync(CreateParticipant("p1", endpoint1).Object)
                .ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.GetAsync(CreateParticipant("p2", endpoint2).Object).ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));

            await lease.CloseAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ManagedGetAsyncShouldCleanupCreatedEntryWhenParticipantFactoryFailsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConfiguredEndpoint endpoint = GetTestEndpoint();
            var channelMock = CreateManagedChannelMock(endpoint);
            var transportBindingsMock = CreateBindings(channelMock, telemetry);
            var manager = new ClientChannelManager(new ApplicationConfiguration(telemetry), telemetry, transportBindingsMock.Object);

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await manager.GetAsync(endpoint, _ => null!, null).ConfigureAwait(false))!;

            Assert.That(ex.Message, Does.Contain("Participant factory returned null."));
            Assert.That(manager.GetChannelDiagnostics(), Is.Empty);
            channelMock.Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
            channelMock.Verify(c => c.Dispose(), Times.Once);
        }

        [Test]
        public void ManagedGetAsyncShouldValidateArguments()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var manager = new ClientChannelManager(new ApplicationConfiguration(telemetry), telemetry);
            ConfiguredEndpoint endpoint = GetTestEndpoint();
            var participant = CreateParticipant("p1", endpoint);
            var participantWithoutEndpoint = new Mock<IReconnectParticipant>();
            participantWithoutEndpoint.SetupGet(p => p.Id).Returns("missing");

            Assert.That(
                async () => await manager.GetAsync((IReconnectParticipant)null!).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await manager.GetAsync(participant.Object, null!).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await manager.GetAsync(participantWithoutEndpoint.Object).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                async () => await manager.GetAsync(null!, _ => participant.Object, null).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await manager.GetAsync(endpoint, null!, null).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                async () => await manager.ReconnectAsync(null!).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void GetIPAddressShouldReturnCorrectIPAddress()
        {
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);

            IPAddress? ipAddress = ClientChannelManager.GetIPAddress(endpoint);

            Assert.That(ipAddress, Is.EqualTo(endpoint.Address));
        }

        [Test]
        public void GetIPAddressShouldReturnIPAddressWhenIPv4IsPreferred()
        {
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);

            IPAddress? ipAddress = ClientChannelManager.GetIPAddress(endpoint, true);

            Assert.That(ipAddress, Is.EqualTo(endpoint.Address));
        }

        [Test]
        public void GetIPAddressShouldReturnCorrectIPAddressWhenIPv4IsPreferred()
        {
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1").MapToIPv6(), 4840);

            IPAddress? ipAddress = ClientChannelManager.GetIPAddress(endpoint, true);

            Assert.That(ipAddress, Is.Not.Null);
            Assert.That(ipAddress!.MapToIPv6(), Is.EqualTo(endpoint.Address));
            Assert.That(ipAddress!.AddressFamily, Is.EqualTo(AddressFamily.InterNetwork));
        }

        [Test]
        public void GetIPAddressShouldReturnIPv6AddressWhenIPv4IsNotRequired()
        {
            const string ipv6AddressString = "0123:4567:89ab:cdef:0123:4567:89ab:cdef";
            var endpoint = new IPEndPoint(IPAddress.Parse(ipv6AddressString), 4840);

            IPAddress? ipAddress = ClientChannelManager.GetIPAddress(endpoint, true);

            Assert.That(ipAddress, Is.EqualTo(endpoint.Address));
            Assert.That(ipAddress!.AddressFamily, Is.EqualTo(AddressFamily.InterNetworkV6));
        }

        [Test]
        public void GetIPAddressShouldReturnNullWhenEndpointIsNotIPEndPoint()
        {
            var endpoint = new DnsEndPoint("localhost", 4840);

            IPAddress? ipAddress = ClientChannelManager.GetIPAddress(endpoint);

            Assert.That(ipAddress, Is.Null);
        }

        [Test]
        public void GetPortShouldReturnCorrectPort()
        {
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);

            int port = ClientChannelManager.GetPort(endpoint);

            Assert.That(port, Is.EqualTo(endpoint.Port));
        }

        [Test]
        public void GetPortShouldReturnMinusOneWhenEndpointIsNotIPEndPoint()
        {
            var endpoint = new DnsEndPoint("localhost", 4840);

            int port = ClientChannelManager.GetPort(endpoint);

            Assert.That(port, Is.EqualTo(-1));
        }

        private static ConfiguredEndpoint GetTestEndpoint(Certificate serverCert)
        {
            var endpoint = new ConfiguredEndpoint
            {
                Configuration = new EndpointConfiguration
                {
                    OperationTimeout = 6000
                }
            };
            endpoint.Description.EndpointUrl = "opc.tcp://localhost:4840";
            endpoint.Description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            endpoint.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            endpoint.Description.ServerCertificate = serverCert.RawData.ToByteString();
            return endpoint;
        }

        private static ConfiguredEndpoint GetTestEndpoint(string endpointUrl = "opc.tcp://localhost:4840")
        {
            var endpoint = new ConfiguredEndpoint
            {
                Configuration = new EndpointConfiguration
                {
                    OperationTimeout = 6000
                }
            };
            endpoint.Description.EndpointUrl = endpointUrl;
            endpoint.Description.SecurityMode = MessageSecurityMode.None;
            endpoint.Description.SecurityPolicyUri = SecurityPolicies.None;
            return endpoint;
        }

        private static Mock<IReconnectParticipant> CreateParticipant(string id, ConfiguredEndpoint endpoint)
        {
            var participant = new Mock<IReconnectParticipant>();
            participant.SetupGet(p => p.Id).Returns(id);
            participant.SetupGet(p => p.Endpoint).Returns(endpoint);
            participant
                .Setup(p => p.OnReconnectAsync(
                    It.IsAny<IManagedTransportChannel>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ParticipantReconnectResult>(ParticipantReconnectResult.Reactivated));
            return participant;
        }

        private static Mock<IChannel> CreateManagedChannelMock(ConfiguredEndpoint endpoint)
        {
            var channelMock = new Mock<IChannel>();
            channelMock.SetupGet(c => c.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
            channelMock.SetupGet(c => c.EndpointDescription).Returns(endpoint.Description);
            channelMock.SetupGet(c => c.EndpointConfiguration).Returns(endpoint.Configuration!);
            channelMock.SetupGet(c => c.ChannelThumbprint).Returns([1, 2, 3]);
            channelMock.SetupGet(c => c.ClientChannelCertificate).Returns([4, 5, 6]);
            channelMock.SetupGet(c => c.ServerChannelCertificate).Returns([7, 8, 9]);
            channelMock.SetupGet(c => c.MessageContext).Returns(ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
            channelMock
                .Setup(c => c.OpenAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            channelMock
                .Setup(c => c.ReconnectAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            channelMock
                .Setup(c => c.CloseAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            return channelMock;
        }

        private static Mock<ITransportChannelBindings> CreateBindings(
            Mock<IChannel> channelMock,
            ITelemetryContext telemetry)
        {
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock
                .Setup(b => b.Create(It.IsAny<string>(), telemetry))
                .Returns(channelMock.Object);
            return transportBindingsMock;
        }

        public interface IChannel : ITransportChannel, ISecureChannel;
    }
}
