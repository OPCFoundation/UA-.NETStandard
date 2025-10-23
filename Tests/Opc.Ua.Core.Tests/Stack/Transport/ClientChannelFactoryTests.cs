/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

using System.Net;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using System;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [Category("ChannelFactory")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ClientChannelFactoryTests
    {
        public ClientChannelFactoryTests()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_transportChannelMock = new Mock<IChannel>();
            m_transportWaitingConnectionMock = new Mock<ITransportWaitingConnection>();
            m_serviceMessageContextMock = new Mock<IServiceMessageContext>();
            m_serviceMessageContextMock.SetupGet(x => x.Telemetry).Returns(m_telemetry);
            m_configuration = new ApplicationConfiguration(m_telemetry);
            m_socketMock = new Mock<IMessageSocket>();
            m_socketFactoryMock = new Mock<IMessageSocketFactory>();
            m_socketFactoryMock
                .Setup(s => s.Create(
                    It.IsAny<IMessageSink>(),
                    It.IsAny<BufferManager>(),
                    It.IsAny<int>()))
                .Returns(m_socketMock.Object);
            m_transportWaitingConnectionMock.Setup(c => c.Handle)
                .Returns(m_socketMock.Object);
            m_transportWaitingConnectionMock.SetupGet(x => x.EndpointUrl)
                .Returns(new Uri("opc.tcp://localhost:4840"));
        }

        [Test]
        public async Task CreateChannelShouldCreateChannelWithConnectionAsync()
        {
            // Arrange
            var sut = new ClientChannelManager(m_configuration);
            X509Certificate2 serverCertificate = CertificateFactory.CreateCertificate("CN=server").CreateForRSA();
            X509Certificate2 clientCertificate = CertificateFactory.CreateCertificate("CN=client").CreateForRSA();
            var clientCertificateChain = new X509Certificate2Collection();

            // Act
            ITransportChannel channel = await sut.CreateChannelAsync(
                GetTestEndpoint(serverCertificate),
                m_serviceMessageContextMock.Object,
                clientCertificate,
                clientCertificateChain,
                m_transportWaitingConnectionMock.Object).ConfigureAwait(false);

            // Assert
            Assert.That(channel, Is.Not.Null);
        }

        [Test]
        public async Task CreateChannelShouldCreateChannelAsync()
        {
            // Arrange
            var sut = new ClientChannelManager(m_configuration);
            X509Certificate2 serverCertificate = CertificateFactory.CreateCertificate("CN=server").CreateForRSA();
            X509Certificate2 clientCertificate = CertificateFactory.CreateCertificate("CN=client").CreateForRSA();
            var clientCertificateChain = new X509Certificate2Collection();

            // Act
            ITransportChannel channel = await sut.CreateChannelAsync(
                GetTestEndpoint(serverCertificate),
                m_serviceMessageContextMock.Object,
                clientCertificate,
                clientCertificateChain).ConfigureAwait(false);

            // Assert
            Assert.That(channel, Is.Not.Null);
        }

        [Test]
        public void CloseChannelShouldDisposeChannel()
        {
            // Arrange
            var sut = new ClientChannelManager(m_configuration);
            m_transportChannelMock
                    .SetupAdd(c => c.OnTokenActivated += It.IsAny<ChannelTokenActivatedEventHandler>())
                .Verifiable(Times.Once);

            // Act
            sut.CloseChannel(m_transportChannelMock.Object);

            // Assert
            m_transportChannelMock.VerifyRemove(
                c => c.OnTokenActivated -= It.IsAny<ChannelTokenActivatedEventHandler>(), Times.Once);
            m_transportChannelMock.Verify(c => c.Dispose(), Times.Once);
        }

        [Test]
        public void OnChannelTokenActivatedShouldInvokeOnDiagnosticsButWithoutEndpointsForNonTcpChannels()
        {
            // Arrange
            var sut = new ClientChannelManager(m_configuration);
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
            ITransportChannel transportChannel = Mock.Of<ITransportChannel>();
            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
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
        public async Task OnChannelTokenActivatedShouldInvokeOnDiagnosticsAsync()
        {
            // Arrange
            var sut = new ClientChannelManager(m_configuration);
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
            (UaSCUaBinaryTransportChannel transportChannel, IPEndPoint? remoteEndPoint, IPEndPoint? localEndPoint)
                = await CreateUaSCUaBinaryTransportChannelWithEndpointsAsync().ConfigureAwait(false);
            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic!.ChannelId, Is.EqualTo(token.ChannelId));
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
        public async Task OnChannelTokenActivatedWithMissingVectorShouldInvokeOnDiagnosticsAsync()
        {
            // Arrange
            var sut = new ClientChannelManager(m_configuration);
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
            (UaSCUaBinaryTransportChannel transportChannel, IPEndPoint? remoteEndPoint, IPEndPoint? localEndPoint)
                = await CreateUaSCUaBinaryTransportChannelWithEndpointsAsync().ConfigureAwait(false);
            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

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
        public async Task OnChannelTokenActivatedWithMissingKeyShouldInvokeOnDiagnostics1Async()
        {
            // Arrange
            var sut = new ClientChannelManager(m_configuration);
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
            (UaSCUaBinaryTransportChannel transportChannel, IPEndPoint? remoteEndPoint, IPEndPoint? localEndPoint)
                = await CreateUaSCUaBinaryTransportChannelWithEndpointsAsync().ConfigureAwait(false);
            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

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
        public async Task OnChannelTokenActivatedWithMissingKeyShouldInvokeOnDiagnostics2Async()
        {
            // Arrange
            var sut = new ClientChannelManager(m_configuration);
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
            (UaSCUaBinaryTransportChannel transportChannel, IPEndPoint? remoteEndPoint, IPEndPoint? localEndPoint)
                = await CreateUaSCUaBinaryTransportChannelWithEndpointsAsync().ConfigureAwait(false);
            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

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
        public async Task OnChannelTokenActivatedOnNonSocketChannelShouldInvokeOnDiagnosticsAsync()
        {
            // Arrange
            var sut = new ClientChannelManager(m_configuration);
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
            UaSCUaBinaryTransportChannel transportChannel =
                await CreateUaSCUaBinaryTransportChannelAsync().ConfigureAwait(false);
            TransportChannelDiagnostic? diagnostic = null;
            sut.OnDiagnostics += (ch, diag) => diagnostic = diag;

            // Act
            sut.OnChannelTokenActivated(transportChannel, token, null);

            // Assert
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
            // Arrange
            var sut = new ClientChannelManager(m_configuration);
            var transportChannel = new Mock<ITransportChannel>();

            TransportChannelDiagnostic? diagnostic = null;
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
            var sut = new ClientChannelManager(m_configuration);
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
            IPAddress? ipAddress = ClientChannelManager.GetIPAddress(endpoint);

            // Assert
            Assert.That(ipAddress, Is.EqualTo(endpoint.Address));
        }

        [Test]
        public void GetIPAddressShouldReturnIPAddressWhenIPv4IsPreferred()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);

            // Act
            IPAddress? ipAddress = ClientChannelManager.GetIPAddress(endpoint, true);

            // Assert
            Assert.That(ipAddress, Is.EqualTo(endpoint.Address));
        }

        [Test]
        public void GetIPAddressShouldReturnCorrectIPAddressWhenIPv4IsPreferred()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1").MapToIPv6(), 4840);

            // Act
            IPAddress? ipAddress = ClientChannelManager.GetIPAddress(endpoint, true);

            // Assert
            Assert.That(ipAddress, Is.Not.Null);
            Assert.That(ipAddress!.MapToIPv6(), Is.EqualTo(endpoint.Address));
            Assert.That(ipAddress!.AddressFamily, Is.EqualTo(AddressFamily.InterNetwork));
        }

        [Test]
        public void GetIPAddressShouldReturnIPv6AddressWhenIPv4IsNotRequired()
        {
            // Arrange
            const string ipv6AddressString = "0123:4567:89ab:cdef:0123:4567:89ab:cdef";
            var endpoint = new IPEndPoint(IPAddress.Parse(ipv6AddressString), 4840);

            // Act
            IPAddress? ipAddress = ClientChannelManager.GetIPAddress(endpoint, true);

            // Assert
            Assert.That(ipAddress, Is.EqualTo(endpoint.Address));
            Assert.That(ipAddress!.AddressFamily, Is.EqualTo(AddressFamily.InterNetworkV6));
        }

        [Test]
        public void GetIPAddressShouldReturnNullWhenEndpointIsNotIPEndPoint()
        {
            // Arrange
            var endpoint = new DnsEndPoint("localhost", 4840);

            // Act
            IPAddress? ipAddress = ClientChannelManager.GetIPAddress(endpoint);

            // Assert
            Assert.That(ipAddress, Is.Null);
        }

        [Test]
        public void GetPortShouldReturnCorrectPort()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);

            // Act
            int port = ClientChannelManager.GetPort(endpoint);

            // Assert
            Assert.That(port, Is.EqualTo(endpoint.Port));
        }

        [Test]
        public void GetPortShouldReturnMinusOneWhenEndpointIsNotIPEndPoint()
        {
            // Arrange
            var endpoint = new DnsEndPoint("localhost", 4840);

            // Act
            int port = ClientChannelManager.GetPort(endpoint);

            // Assert
            Assert.That(port, Is.EqualTo(-1));
        }

        internal record UaSCUaBinaryChannel(
            UaSCUaBinaryTransportChannel Channel,
            IPEndPoint RemoteEndPoint,
            IPEndPoint LocalEndPoint);

        private async Task<UaSCUaBinaryChannel> CreateUaSCUaBinaryTransportChannelWithEndpointsAsync()
        {
            var remoteEndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4840);
            var localEndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.2"), 4841);
            m_socketMock.Setup(s => s.RemoteEndpoint).Returns(remoteEndPoint);
            m_socketMock.Setup(s => s.LocalEndpoint).Returns(localEndPoint);
            var transportChannel = new UaSCUaBinaryTransportChannel(m_socketFactoryMock.Object, m_telemetry);
            X509Certificate2 serverCert = CertificateFactory.CreateCertificate("CN=server").CreateForRSA();
            X509Certificate2 clientCert = CertificateFactory.CreateCertificate("CN=client").CreateForRSA();
            ConfiguredEndpoint testEndpoint = GetTestEndpoint(serverCert);
            await transportChannel.OpenAsync(m_transportWaitingConnectionMock.Object, new TransportChannelSettings
            {
                Configuration = testEndpoint.Configuration,
                CertificateValidator = new CertificateValidator(m_telemetry),
                ClientCertificate = clientCert,
                ClientCertificateChain = [],
                Factory = EncodeableFactory.Create(),
                ServerCertificate = serverCert,
                NamespaceUris = new NamespaceTable(),
                Description = testEndpoint.Description
            }, default).ConfigureAwait(false);
            return new UaSCUaBinaryChannel(transportChannel, remoteEndPoint, localEndPoint);
        }

        private async Task<UaSCUaBinaryTransportChannel> CreateUaSCUaBinaryTransportChannelAsync()
        {
            var transportChannel = new UaSCUaBinaryTransportChannel(m_socketFactoryMock.Object, m_telemetry);
            X509Certificate2 serverCert = CertificateFactory.CreateCertificate("CN=server").CreateForRSA();
            X509Certificate2 clientCert = CertificateFactory.CreateCertificate("CN=client").CreateForRSA();
            ConfiguredEndpoint testEndpoint = GetTestEndpoint(serverCert);
            await transportChannel.OpenAsync(new Uri("opc.tcp://localhost:4840"), new TransportChannelSettings
            {
                Configuration = testEndpoint.Configuration,
                CertificateValidator = new CertificateValidator(m_telemetry),
                ClientCertificate = clientCert,
                ClientCertificateChain = [],
                Factory = EncodeableFactory.Create(),
                ServerCertificate = serverCert,
                NamespaceUris = new NamespaceTable(),
                Description = testEndpoint.Description
            }, default).ConfigureAwait(false);
            return transportChannel;
        }

        private static ConfiguredEndpoint GetTestEndpoint(X509Certificate2 serverCert)
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
            endpoint.Description.ServerCertificate = serverCert.RawData;
            return endpoint;
        }

        public interface IChannel : ITransportChannel, ISecureChannel;

        private readonly ApplicationConfiguration m_configuration;
        private readonly ITelemetryContext m_telemetry;
        private readonly Mock<IChannel> m_transportChannelMock;
        private readonly Mock<ITransportWaitingConnection> m_transportWaitingConnectionMock;
        private readonly Mock<IServiceMessageContext> m_serviceMessageContextMock;
        private readonly Mock<IMessageSocket> m_socketMock;
        private readonly Mock<IMessageSocketFactory> m_socketFactoryMock;
    }
}
