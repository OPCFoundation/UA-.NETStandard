// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Moq;
using Opc.Ua.Bindings;
using Opc.Ua.Client.Certificates;
using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;

namespace Opc.Ua.Client
{
    [TestFixture]
    public sealed class ChannelFactoryTests
    {
        [SetUp]
        public void SetUp()
        {
            m_observabilityMock = new Mock<ITelemetryContext>();
            m_timeProviderMock = new Mock<TimeProvider>();
            m_transportChannelMock = new Mock<ITransportChannel>();
            m_transportWaitingConnectionMock = new Mock<ITransportWaitingConnection>();
            m_serviceMessageContextMock = new Mock<IServiceMessageContext>();
            m_configuration = new ApplicationConfiguration();
            m_observabilityMock.Setup(o => o.TimeProvider).Returns(m_timeProviderMock.Object);
            m_socketMock = new Mock<IMessageSocket>();
            m_socketFactoryMock = new Mock<IMessageSocketFactory>();
            m_socketFactoryMock
                .Setup(s => s.Create(
                    It.IsAny<IMessageSink>(),
                    It.IsAny<BufferManager>(),
                    It.IsAny<int>()))
                .Returns(m_socketMock.Object);
            m_transportWaitingConnectionMock.Setup(c => c.Handle).Returns(m_socketMock.Object);
        }

        [Test]
        public void CreateChannelShouldCreateChannelWithConnection()
        {
            // Arrange
            var sut = new ChannelFactory(m_configuration, m_observabilityMock.Object);
            var serverCertificate = new X500DistinguishedName("CN=server").CreateSelfSignedCertificate();
            var clientCertificate = new X500DistinguishedName("CN=client").CreateSelfSignedCertificate();
            var clientCertificateChain = new X509Certificate2Collection();

            // Act
            var channel = sut.CreateChannel(GetTestEndpoint(serverCertificate),
                m_serviceMessageContextMock.Object, clientCertificate, clientCertificateChain,
                m_transportWaitingConnectionMock.Object);

            // Assert
            Assert.That(channel, Is.Not.Null);
        }

        [Test]
        public void CreateChannelShouldCreateChannelWithoutConnection()
        {
            // Arrange
            var sut = new ChannelFactory(m_configuration, m_observabilityMock.Object);
            var serverCertificate = new X500DistinguishedName("CN=server").CreateSelfSignedCertificate();
            var clientCertificate = new X500DistinguishedName("CN=client").CreateSelfSignedCertificate();
            var clientCertificateChain = new X509Certificate2Collection();

            // Act
            var channel = sut.CreateChannel(GetTestEndpoint(serverCertificate),
                m_serviceMessageContextMock.Object, clientCertificate, clientCertificateChain);

            // Assert
            Assert.That(channel, Is.Not.Null);
        }

        [Test]
        public void CloseChannelShouldDisposeChannel()
        {
            // Arrange
            var sut = new ChannelFactory(m_configuration, m_observabilityMock.Object);
            m_transportChannelMock
                    .SetupAdd(c => c.OnTokenActivated += It.IsAny<ChannelTokenActivatedEventHandler>())
                .Verifiable(Times.Once);

            // Act
            sut.CloseChannel(m_transportChannelMock.Object);

            // Assert
            m_transportChannelMock.VerifyRemove(c => c.OnTokenActivated -= It.IsAny<ChannelTokenActivatedEventHandler>(), Times.Once);
            m_transportChannelMock.Verify(c => c.Dispose(), Times.Once);
        }

        [Test]
        public void OnChannelTokenActivatedShouldInvokeOnDiagnosticsButWithoutEndpointsForNonTcpChannels()
        {
            // Arrange
            var sut = new ChannelFactory(m_configuration, m_observabilityMock.Object);
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
            var transportChannel = Mock.Of<ITransportChannel>();
            m_timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            Assert.That(diagnostic, Is.Not.Null);
            diagnostic!.Assert.That(ChannelId, Is.EqualTo(token.ChannelId));
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
            // Arrange
            var sut = new ChannelFactory(m_configuration, m_observabilityMock.Object);
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
            var transportChannel = CreateUaSCUaBinaryTransportChannel(out var remoteEndPoint, out var localEndPoint);
            m_timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            Assert.That(diagnostic, Is.Not.Null);
            diagnostic!.Assert.That(ChannelId, Is.EqualTo(token.ChannelId));
            Assert.That(diagnostic.TokenId, Is.EqualTo(token.TokenId));
            Assert.That(diagnostic.CreatedAt, Is.EqualTo(token.CreatedAt));
            Assert.That(diagnostic.Lifetime, Is.EqualTo(TimeSpan.FromMilliseconds(token.Lifetime)));
            Assert.That(diagnostic.Client, Is.Not.Null);
            Assert.That(diagnostic.Server, Is.Not.Null);
            Assert.That(diagnostic.RemoteIpAddress, Is.EqualTo(remoteEndPoint.Address));
            Assert.That(diagnostic.RemotePort, Is.EqualTo(remoteEndPoint.Port));
            Assert.That(diagnostic.LocalIpAddress, Is.EqualTo(localEndPoint.Address));
            Assert.That(diagnostic.LocalPort, Is.EqualTo(localEndPoint.Port));
        }

        [Test]
        public void OnChannelTokenActivatedWithMissingVectorShouldInvokeOnDiagnostics()
        {
            // Arrange
            var sut = new ChannelFactory(m_configuration, m_observabilityMock.Object);
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
            var transportChannel = CreateUaSCUaBinaryTransportChannel(out var remoteEndPoint, out var localEndPoint);
            m_timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            Assert.That(diagnostic, Is.Not.Null);
            diagnostic!.Assert.That(ChannelId, Is.EqualTo(token.ChannelId));
            Assert.That(diagnostic.TokenId, Is.EqualTo(token.TokenId));
            Assert.That(diagnostic.CreatedAt, Is.EqualTo(token.CreatedAt));
            Assert.That(diagnostic.Lifetime, Is.EqualTo(TimeSpan.FromMilliseconds(token.Lifetime)));
            Assert.That(diagnostic.Client, Is.Null);
            Assert.That(diagnostic.Server, Is.Null);
        }

        [Test]
        public void OnChannelTokenActivatedWithMissingKeyShouldInvokeOnDiagnostics1()
        {
            // Arrange
            var sut = new ChannelFactory(m_configuration, m_observabilityMock.Object);
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
            var transportChannel = CreateUaSCUaBinaryTransportChannel(out var remoteEndPoint, out var localEndPoint);
            m_timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            Assert.That(diagnostic, Is.Not.Null);
            diagnostic!.Assert.That(ChannelId, Is.EqualTo(token.ChannelId));
            Assert.That(diagnostic.TokenId, Is.EqualTo(token.TokenId));
            Assert.That(diagnostic.CreatedAt, Is.EqualTo(token.CreatedAt));
            Assert.That(diagnostic.Lifetime, Is.EqualTo(TimeSpan.FromMilliseconds(token.Lifetime)));
            Assert.That(diagnostic.Client, Is.Null);
            Assert.That(diagnostic.Server, Is.Null);
        }

        [Test]
        public void OnChannelTokenActivatedWithMissingKeyShouldInvokeOnDiagnostics2()
        {
            // Arrange
            var sut = new ChannelFactory(m_configuration, m_observabilityMock.Object);
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
            var transportChannel = CreateUaSCUaBinaryTransportChannel(out var remoteEndPoint, out var localEndPoint);
            m_timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            Assert.That(diagnostic, Is.Not.Null);
            diagnostic!.Assert.That(ChannelId, Is.EqualTo(token.ChannelId));
            Assert.That(diagnostic.TokenId, Is.EqualTo(token.TokenId));
            Assert.That(diagnostic.CreatedAt, Is.EqualTo(token.CreatedAt));
            Assert.That(diagnostic.Lifetime, Is.EqualTo(TimeSpan.FromMilliseconds(token.Lifetime)));
            Assert.That(diagnostic.Client, Is.Null);
            Assert.That(diagnostic.Server, Is.Null);
        }

        [Test]
        public void OnChannelTokenActivatedOnNonSocketChannelShouldInvokeOnDiagnostics()
        {
            // Arrange
            var sut = new ChannelFactory(m_configuration, m_observabilityMock.Object);
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
            var transportChannel = CreateUaSCUaBinaryTransportChannel();
            m_timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            Assert.That(diagnostic, Is.Not.Null);
            diagnostic!.Assert.That(ChannelId, Is.EqualTo(token.ChannelId));
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
            // Arrange
            var sut = new ChannelFactory(m_configuration, m_observabilityMock.Object);
            var transportChannel = new Mock<ITransportChannel>();

            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel.Object, null, null);

            // Assert
            Assert.That(diagnostic, Is.Null);
        }

        [Test]
        public void OnChannelTokenActivatedShouldNotThrowWhenOnDiagnosticsIsNull()
        {
            // Arrange
            var sut = new ChannelFactory(m_configuration, m_observabilityMock.Object);
            var transportChannel = new Mock<ITransportChannel>();

            // Act
            sut.OnChannelTokenActivated(transportChannel.Object, null, null);
        }

        [Test]
        public void GetIPAddressShouldReturnCorrectIPAddress()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);

            // Act
            var ipAddress = ChannelFactory.GetIPAddress(endpoint);

            // Assert
            Assert.That(ipAddress, Is.EqualTo(endpoint.Address));
        }

        [Test]
        public void GetIPAddressShouldReturnIPAddressWhenIPv4IsPreferred()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);

            // Act
            var ipAddress = ChannelFactory.GetIPAddress(endpoint, true);

            // Assert
            Assert.That(ipAddress, Is.EqualTo(endpoint.Address));
        }

        [Test]
        public void GetIPAddressShouldReturnCorrectIPAddressWhenIPv4IsPreferred()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1").MapToIPv6(), 4840);

            // Act
            var ipAddress = ChannelFactory.GetIPAddress(endpoint, true);

            // Assert
            Assert.That(ipAddress, Is.Not.Null);
            Assert.That(ipAddress!.MapToIPv6(), Is.EqualTo(endpoint.Address));
            ipAddress!.Assert.That(AddressFamily, Is.EqualTo(AddressFamily.InterNetwork));
        }

        [Test]
        public void GetIPAddressShouldReturnIPv6AddressWhenIPv4IsNotRequired()
        {
            // Arrange
            const string ipv6AddressString = "0123:4567:89ab:cdef:0123:4567:89ab:cdef";
            var endpoint = new IPEndPoint(IPAddress.Parse(ipv6AddressString), 4840);

            // Act
            var ipAddress = ChannelFactory.GetIPAddress(endpoint, true);

            // Assert
            Assert.That(ipAddress, Is.EqualTo(endpoint.Address));
            ipAddress!.Assert.That(AddressFamily, Is.EqualTo(AddressFamily.InterNetworkV6));
        }

        [Test]
        public void GetIPAddressShouldReturnNullWhenEndpointIsNotIPEndPoint()
        {
            // Arrange
            var endpoint = new DnsEndPoint("localhost", 4840);

            // Act
            var ipAddress = ChannelFactory.GetIPAddress(endpoint);

            // Assert
            Assert.That(ipAddress, Is.Null);
        }

        [Test]
        public void GetPortShouldReturnCorrectPort()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);

            // Act
            var port = ChannelFactory.GetPort(endpoint);

            // Assert
            Assert.That(port, Is.EqualTo(endpoint.Port));
        }

        [Test]
        public void GetPortShouldReturnMinusOneWhenEndpointIsNotIPEndPoint()
        {
            // Arrange
            var endpoint = new DnsEndPoint("localhost", 4840);

            // Act
            var port = ChannelFactory.GetPort(endpoint);

            // Assert
            Assert.That(port, Is.EqualTo(-1));
        }

        private UaSCUaBinaryTransportChannel CreateUaSCUaBinaryTransportChannel(
            out IPEndPoint remoteEndPoint, out IPEndPoint localEndPoint)
        {
            remoteEndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);
            localEndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.2"), 4841);
            m_socketMock.Setup(s => s.RemoteEndpoint).Returns(remoteEndPoint);
            m_socketMock.Setup(s => s.LocalEndpoint).Returns(localEndPoint);
            var transportChannel = new UaSCUaBinaryTransportChannel(m_socketFactoryMock.Object);
            var serverCert = new X500DistinguishedName("CN=server").CreateSelfSignedCertificate();
            var clientCert = new X500DistinguishedName("CN=client").CreateSelfSignedCertificate();
            var testEndpoint = GetTestEndpoint(serverCert);
            transportChannel.Initialize(m_transportWaitingConnectionMock.Object, new TransportChannelSettings
            {
                Configuration = testEndpoint.Configuration,
                CertificateValidator = new CertificateValidator(),
                ClientCertificate = clientCert,
                ClientCertificateChain = [],
                Factory = new EncodeableFactory(),
                ServerCertificate = serverCert,
                NamespaceUris = new NamespaceTable(),
                Description = testEndpoint.Description
            });
            return transportChannel;
        }

        private UaSCUaBinaryTransportChannel CreateUaSCUaBinaryTransportChannel()
        {
            var transportChannel = new UaSCUaBinaryTransportChannel(m_socketFactoryMock.Object);
            var serverCert = new X500DistinguishedName("CN=server").CreateSelfSignedCertificate();
            var clientCert = new X500DistinguishedName("CN=client").CreateSelfSignedCertificate();
            var testEndpoint = GetTestEndpoint(serverCert);
            transportChannel.Initialize(new Uri("opc.tcp://localhost:4840"), new TransportChannelSettings
            {
                Configuration = testEndpoint.Configuration,
                CertificateValidator = new CertificateValidator(),
                ClientCertificate = clientCert,
                ClientCertificateChain = [],
                Factory = new EncodeableFactory(),
                ServerCertificate = serverCert,
                NamespaceUris = new NamespaceTable(),
                Description = testEndpoint.Description
            });
            return transportChannel;
        }

        private static ConfiguredEndpoint GetTestEndpoint(X509Certificate2 serverCert)
        {
            var endpoint = new ConfiguredEndpoint
            {
                Configuration = new EndpointConfiguration()
            };
            endpoint.Description.EndpointUrl = "opc.tcp://localhost:4840";
            endpoint.Description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            endpoint.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            endpoint.Description.ServerCertificate = serverCert.RawData;
            return endpoint;
        }

        private ApplicationConfiguration m_configuration;
        private Mock<ITelemetryContext> m_observabilityMock;
        private Mock<TimeProvider> m_timeProviderMock;
        private Mock<ITransportChannel> m_transportChannelMock;
        private Mock<ITransportWaitingConnection> m_transportWaitingConnectionMock;
        private Mock<IServiceMessageContext> m_serviceMessageContextMock;
        private Mock<IMessageSocket> m_socketMock;
        private Mock<IMessageSocketFactory> m_socketFactoryMock;
    }
}
