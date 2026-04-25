#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Moq;
using Opc.Ua.Client.Certificates;
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
            m_serviceMessageContextMock = new Mock<IServiceMessageContext>();
            m_configuration = new ApplicationConfiguration();
        }

        [Test]
        public void CreateChannelShouldCreateChannel()
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

        private static ConfiguredEndpoint GetTestEndpoint(X509Certificate2 serverCert)
        {
            var endpoint = new ConfiguredEndpoint
            {
                Configuration = new EndpointConfiguration()
            };
            endpoint.Description.EndpointUrl = "opc.tcp://localhost:4840";
            endpoint.Description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            endpoint.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            endpoint.Description.ServerCertificate = new ByteString(serverCert.RawData);
            return endpoint;
        }

        private ApplicationConfiguration m_configuration = null!;
        private Mock<ITelemetryContext> m_observabilityMock = null!;
        private Mock<IServiceMessageContext> m_serviceMessageContextMock = null!;
    }
}
#endif
