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

using System.Net;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using System;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
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
        [Test]
        public async Task CreateChannelShouldCreateChannelWithConnectionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using X509Certificate2 serverCertificate = CertificateFactory.CreateCertificate("CN=server").CreateForRSA();
            using X509Certificate2 clientCertificate = CertificateFactory.CreateCertificate("CN=client").CreateForRSA();
            var clientCertificateChain = new X509Certificate2Collection();

            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);

            var transportWaitingConnectionMock = new Mock<ITransportWaitingConnection>();
            var serviceMessageContextMock = new Mock<IServiceMessageContext>();
            serviceMessageContextMock.SetupGet(x => x.Telemetry).Returns(telemetry);
            var configuration = new ApplicationConfiguration(telemetry);
            var socket = new Mock<IMessageSocket>();
            transportChannelMock.SetupGet(x => x.Socket).Returns(socket.Object);

            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);

            ITransportChannel channel = await sut.CreateChannelAsync(
                GetTestEndpoint(serverCertificate),
                serviceMessageContextMock.Object,
                clientCertificate,
                clientCertificateChain,
                transportWaitingConnectionMock.Object).ConfigureAwait(false);

            Assert.That(channel, Is.Not.Null);
        }

        [Test]
        public async Task CreateChannelShouldCreateChannelAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using X509Certificate2 serverCertificate = CertificateFactory.CreateCertificate("CN=server").CreateForRSA();
            using X509Certificate2 clientCertificate = CertificateFactory.CreateCertificate("CN=client").CreateForRSA();
            var clientCertificateChain = new X509Certificate2Collection();

            var transportChannelMock = new Mock<IChannel>();
            var transportBindingsMock = new Mock<ITransportChannelBindings>();
            transportBindingsMock.Setup(x => x.Create(It.IsAny<string>(), telemetry))
                .Returns(transportChannelMock.Object);

            var serviceMessageContextMock = new Mock<IServiceMessageContext>();
            serviceMessageContextMock.SetupGet(x => x.Telemetry).Returns(telemetry);
            var configuration = new ApplicationConfiguration(telemetry);
            var socket = new Mock<IMessageSocket>();
            transportChannelMock.SetupGet(x => x.Socket).Returns(socket.Object);

            var sut = new ClientChannelManager(configuration, transportBindingsMock.Object);

            ITransportChannel channel = await sut.CreateChannelAsync(
                GetTestEndpoint(serverCertificate),
                serviceMessageContextMock.Object,
                clientCertificate,
                clientCertificateChain).ConfigureAwait(false);

            Assert.That(channel, Is.Not.Null);
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
            var socket = new Mock<IMessageSocket>();
            var localEndPoint = new IPEndPoint(IPAddress.Parse("1.2.3.4"), 1234);
            var remoteEndPoint = new IPEndPoint(IPAddress.Parse("4.3.2.1"), 4321);
            socket.Setup(s => s.LocalEndpoint).Returns(localEndPoint);
            socket.Setup(s => s.RemoteEndpoint).Returns(remoteEndPoint);
            transportChannelMock.SetupGet(x => x.Socket).Returns(socket.Object);

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
            Assert.That(diagnostic.RemoteIpAddress, Is.EqualTo(remoteEndPoint.Address));
            Assert.That(diagnostic.RemotePort, Is.EqualTo(remoteEndPoint.Port));
            Assert.That(diagnostic.LocalIpAddress, Is.EqualTo(localEndPoint.Address));
            Assert.That(diagnostic.LocalPort, Is.EqualTo(localEndPoint.Port));
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
            endpoint.Description.ServerCertificate = serverCert.RawData.ToByteString();
            return endpoint;
        }

        public interface IChannel : ITransportChannel, ISecureChannel, IMessageSocketChannel;
    }
}
