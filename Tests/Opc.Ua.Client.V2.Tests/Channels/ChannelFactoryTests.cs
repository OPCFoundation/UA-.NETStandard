// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using FluentAssertions;
    using Moq;
    using Opc.Ua.Bindings;
    using Opc.Ua.Client.Certificates;
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography.X509Certificates;
    using Xunit;

    public sealed class ChannelFactoryTests
    {
        public ChannelFactoryTests()
        {
            _observabilityMock = new Mock<ITelemetryContext>();
            _timeProviderMock = new Mock<TimeProvider>();
            _transportChannelMock = new Mock<ITransportChannel>();
            _transportWaitingConnectionMock = new Mock<ITransportWaitingConnection>();
            _serviceMessageContextMock = new Mock<IServiceMessageContext>();
            _configuration = new ApplicationConfiguration();
            _observabilityMock.Setup(o => o.TimeProvider).Returns(_timeProviderMock.Object);
            _socketMock = new Mock<IMessageSocket>();
            _socketFactoryMock = new Mock<IMessageSocketFactory>();
            _socketFactoryMock
                .Setup(s => s.Create(
                    It.IsAny<IMessageSink>(),
                    It.IsAny<BufferManager>(),
                    It.IsAny<int>()))
                .Returns(_socketMock.Object);
            _transportWaitingConnectionMock.Setup(c => c.Handle).Returns(_socketMock.Object);
        }

        [Fact]
        public void CreateChannelShouldCreateChannelWithConnection()
        {
            // Arrange
            var sut = new ChannelFactory(_configuration, _observabilityMock.Object);
            var serverCertificate = new X500DistinguishedName("CN=server").CreateSelfSignedCertificate();
            var clientCertificate = new X500DistinguishedName("CN=client").CreateSelfSignedCertificate();
            var clientCertificateChain = new X509Certificate2Collection();

            // Act
            var channel = sut.CreateChannel(GetTestEndpoint(serverCertificate),
                _serviceMessageContextMock.Object, clientCertificate, clientCertificateChain,
                _transportWaitingConnectionMock.Object);

            // Assert
            channel.Should().NotBeNull();
        }

        [Fact]
        public void CreateChannelShouldCreateChannelWithoutConnection()
        {
            // Arrange
            var sut = new ChannelFactory(_configuration, _observabilityMock.Object);
            var serverCertificate = new X500DistinguishedName("CN=server").CreateSelfSignedCertificate();
            var clientCertificate = new X500DistinguishedName("CN=client").CreateSelfSignedCertificate();
            var clientCertificateChain = new X509Certificate2Collection();

            // Act
            var channel = sut.CreateChannel(GetTestEndpoint(serverCertificate),
                _serviceMessageContextMock.Object, clientCertificate, clientCertificateChain);

            // Assert
            channel.Should().NotBeNull();
        }

        [Fact]
        public void CloseChannelShouldDisposeChannel()
        {
            // Arrange
            var sut = new ChannelFactory(_configuration, _observabilityMock.Object);
            _transportChannelMock
                    .SetupAdd(c => c.OnTokenActivated += It.IsAny<ChannelTokenActivatedEventHandler>())
                .Verifiable(Times.Once);

            // Act
            sut.CloseChannel(_transportChannelMock.Object);

            // Assert
            _transportChannelMock.VerifyRemove(c => c.OnTokenActivated -= It.IsAny<ChannelTokenActivatedEventHandler>(), Times.Once);
            _transportChannelMock.Verify(c => c.Dispose(), Times.Once);
        }

        [Fact]
        public void OnChannelTokenActivatedShouldInvokeOnDiagnosticsButWithoutEndpointsForNonTcpChannels()
        {
            // Arrange
            var sut = new ChannelFactory(_configuration, _observabilityMock.Object);
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
            _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            diagnostic.Should().NotBeNull();
            diagnostic!.ChannelId.Should().Be(token.ChannelId);
            diagnostic.TokenId.Should().Be(token.TokenId);
            diagnostic.CreatedAt.Should().Be(token.CreatedAt);
            diagnostic.Lifetime.Should().Be(TimeSpan.FromMilliseconds(token.Lifetime));
            diagnostic.Client.Should().NotBeNull();
            diagnostic.Server.Should().NotBeNull();
            diagnostic.RemoteIpAddress.Should().BeNull();
            diagnostic.RemotePort.Should().BeNull();
            diagnostic.LocalIpAddress.Should().BeNull();
            diagnostic.LocalPort.Should().BeNull();
        }

        [Fact]
        public void OnChannelTokenActivatedShouldInvokeOnDiagnostics()
        {
            // Arrange
            var sut = new ChannelFactory(_configuration, _observabilityMock.Object);
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
            _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            diagnostic.Should().NotBeNull();
            diagnostic!.ChannelId.Should().Be(token.ChannelId);
            diagnostic.TokenId.Should().Be(token.TokenId);
            diagnostic.CreatedAt.Should().Be(token.CreatedAt);
            diagnostic.Lifetime.Should().Be(TimeSpan.FromMilliseconds(token.Lifetime));
            diagnostic.Client.Should().NotBeNull();
            diagnostic.Server.Should().NotBeNull();
            diagnostic.RemoteIpAddress.Should().Be(remoteEndPoint.Address);
            diagnostic.RemotePort.Should().Be(remoteEndPoint.Port);
            diagnostic.LocalIpAddress.Should().Be(localEndPoint.Address);
            diagnostic.LocalPort.Should().Be(localEndPoint.Port);
        }

        [Fact]
        public void OnChannelTokenActivatedWithMissingVectorShouldInvokeOnDiagnostics()
        {
            // Arrange
            var sut = new ChannelFactory(_configuration, _observabilityMock.Object);
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
            _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            diagnostic.Should().NotBeNull();
            diagnostic!.ChannelId.Should().Be(token.ChannelId);
            diagnostic.TokenId.Should().Be(token.TokenId);
            diagnostic.CreatedAt.Should().Be(token.CreatedAt);
            diagnostic.Lifetime.Should().Be(TimeSpan.FromMilliseconds(token.Lifetime));
            diagnostic.Client.Should().BeNull();
            diagnostic.Server.Should().BeNull();
        }

        [Fact]
        public void OnChannelTokenActivatedWithMissingKeyShouldInvokeOnDiagnostics1()
        {
            // Arrange
            var sut = new ChannelFactory(_configuration, _observabilityMock.Object);
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
            _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            diagnostic.Should().NotBeNull();
            diagnostic!.ChannelId.Should().Be(token.ChannelId);
            diagnostic.TokenId.Should().Be(token.TokenId);
            diagnostic.CreatedAt.Should().Be(token.CreatedAt);
            diagnostic.Lifetime.Should().Be(TimeSpan.FromMilliseconds(token.Lifetime));
            diagnostic.Client.Should().BeNull();
            diagnostic.Server.Should().BeNull();
        }

        [Fact]
        public void OnChannelTokenActivatedWithMissingKeyShouldInvokeOnDiagnostics2()
        {
            // Arrange
            var sut = new ChannelFactory(_configuration, _observabilityMock.Object);
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
            _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            diagnostic.Should().NotBeNull();
            diagnostic!.ChannelId.Should().Be(token.ChannelId);
            diagnostic.TokenId.Should().Be(token.TokenId);
            diagnostic.CreatedAt.Should().Be(token.CreatedAt);
            diagnostic.Lifetime.Should().Be(TimeSpan.FromMilliseconds(token.Lifetime));
            diagnostic.Client.Should().BeNull();
            diagnostic.Server.Should().BeNull();
        }

        [Fact]
        public void OnChannelTokenActivatedOnNonSocketChannelShouldInvokeOnDiagnostics()
        {
            // Arrange
            var sut = new ChannelFactory(_configuration, _observabilityMock.Object);
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
            _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            diagnostic.Should().NotBeNull();
            diagnostic!.ChannelId.Should().Be(token.ChannelId);
            diagnostic.TokenId.Should().Be(token.TokenId);
            diagnostic.CreatedAt.Should().Be(token.CreatedAt);
            diagnostic.Lifetime.Should().Be(TimeSpan.FromMilliseconds(token.Lifetime));
            diagnostic.Client.Should().NotBeNull();
            diagnostic.Server.Should().NotBeNull();
            diagnostic.RemoteIpAddress.Should().BeNull();
            diagnostic.RemotePort.Should().BeNull();
            diagnostic.LocalIpAddress.Should().BeNull();
            diagnostic.LocalPort.Should().BeNull();
        }

        [Fact]
        public void OnChannelTokenActivatedShouldNotInvokeOnDiagnosticsWhenTokenIsNull()
        {
            // Arrange
            var sut = new ChannelFactory(_configuration, _observabilityMock.Object);
            var transportChannel = new Mock<ITransportChannel>();

            ChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel.Object, null, null);

            // Assert
            diagnostic.Should().BeNull();
        }

        [Fact]
        public void OnChannelTokenActivatedShouldNotThrowWhenOnDiagnosticsIsNull()
        {
            // Arrange
            var sut = new ChannelFactory(_configuration, _observabilityMock.Object);
            var transportChannel = new Mock<ITransportChannel>();

            // Act
            sut.OnChannelTokenActivated(transportChannel.Object, null, null);
        }

        [Fact]
        public void GetIPAddressShouldReturnCorrectIPAddress()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);

            // Act
            var ipAddress = ChannelFactory.GetIPAddress(endpoint);

            // Assert
            ipAddress.Should().Be(endpoint.Address);
        }

        [Fact]
        public void GetIPAddressShouldReturnIPAddressWhenIPv4IsPreferred()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);

            // Act
            var ipAddress = ChannelFactory.GetIPAddress(endpoint, true);

            // Assert
            ipAddress.Should().Be(endpoint.Address);
        }

        [Fact]
        public void GetIPAddressShouldReturnCorrectIPAddressWhenIPv4IsPreferred()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1").MapToIPv6(), 4840);

            // Act
            var ipAddress = ChannelFactory.GetIPAddress(endpoint, true);

            // Assert
            ipAddress.Should().NotBeNull();
            ipAddress!.MapToIPv6().Should().Be(endpoint.Address);
            ipAddress!.AddressFamily.Should().Be(AddressFamily.InterNetwork);
        }

        [Fact]
        public void GetIPAddressShouldReturnIPv6AddressWhenIPv4IsNotRequired()
        {
            // Arrange
            const string ipv6AddressString = "0123:4567:89ab:cdef:0123:4567:89ab:cdef";
            var endpoint = new IPEndPoint(IPAddress.Parse(ipv6AddressString), 4840);

            // Act
            var ipAddress = ChannelFactory.GetIPAddress(endpoint, true);

            // Assert
            ipAddress.Should().Be(endpoint.Address);
            ipAddress!.AddressFamily.Should().Be(AddressFamily.InterNetworkV6);
        }

        [Fact]
        public void GetIPAddressShouldReturnNullWhenEndpointIsNotIPEndPoint()
        {
            // Arrange
            var endpoint = new DnsEndPoint("localhost", 4840);

            // Act
            var ipAddress = ChannelFactory.GetIPAddress(endpoint);

            // Assert
            ipAddress.Should().BeNull();
        }

        [Fact]
        public void GetPortShouldReturnCorrectPort()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);

            // Act
            var port = ChannelFactory.GetPort(endpoint);

            // Assert
            port.Should().Be(endpoint.Port);
        }

        [Fact]
        public void GetPortShouldReturnMinusOneWhenEndpointIsNotIPEndPoint()
        {
            // Arrange
            var endpoint = new DnsEndPoint("localhost", 4840);

            // Act
            var port = ChannelFactory.GetPort(endpoint);

            // Assert
            port.Should().Be(-1);
        }

        private UaSCUaBinaryTransportChannel CreateUaSCUaBinaryTransportChannel(
            out IPEndPoint remoteEndPoint, out IPEndPoint localEndPoint)
        {
            remoteEndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);
            localEndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.2"), 4841);
            _socketMock.Setup(s => s.RemoteEndpoint).Returns(remoteEndPoint);
            _socketMock.Setup(s => s.LocalEndpoint).Returns(localEndPoint);
            var transportChannel = new UaSCUaBinaryTransportChannel(_socketFactoryMock.Object);
            var serverCert = new X500DistinguishedName("CN=server").CreateSelfSignedCertificate();
            var clientCert = new X500DistinguishedName("CN=client").CreateSelfSignedCertificate();
            var testEndpoint = GetTestEndpoint(serverCert);
            transportChannel.Initialize(_transportWaitingConnectionMock.Object, new TransportChannelSettings
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
            var transportChannel = new UaSCUaBinaryTransportChannel(_socketFactoryMock.Object);
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

        private readonly ApplicationConfiguration _configuration;
        private readonly Mock<ITelemetryContext> _observabilityMock;
        private readonly Mock<TimeProvider> _timeProviderMock;
        private readonly Mock<ITransportChannel> _transportChannelMock;
        private readonly Mock<ITransportWaitingConnection> _transportWaitingConnectionMock;
        private readonly Mock<IServiceMessageContext> _serviceMessageContextMock;
        private readonly Mock<IMessageSocket> _socketMock;
        private readonly Mock<IMessageSocketFactory> _socketFactoryMock;
    }
}
