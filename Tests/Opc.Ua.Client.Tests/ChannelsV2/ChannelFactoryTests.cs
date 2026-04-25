#if OPCUA_CLIENT_V2
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
