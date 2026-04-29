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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("DefaultSessionFactory")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class DefaultSessionFactoryTests
    {
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void DefaultSessionFactoryCreateReturnsManagedSession()
        {
            var factory = new ManagedSessionFactory(m_telemetry);
            var channel = new Mock<ITransportChannel>();
            channel
                .SetupGet(c => c.MessageContext)
                .Returns(ServiceMessageContext.Create(m_telemetry));
            channel
                .SetupGet(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            var configuration = new ApplicationConfiguration(m_telemetry)
            {
                ClientConfiguration = new ClientConfiguration()
            };

            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            });

            // DefaultSessionFactory.Create delegates to ClassicSessionFactory.Create
            // which returns a raw Session (not ManagedSession) for the synchronous overload.
            ISession session = factory.Create(channel.Object, configuration, endpoint);

            Assert.That(session, Is.Not.Null);
            Assert.That(session, Is.InstanceOf<Session>());

            session.Dispose();
        }

        [Test]
        public void ClassicSessionFactoryCreateReturnsSession()
        {
            var factory = new DefaultSessionFactory(m_telemetry);
            var channel = new Mock<ITransportChannel>();
            channel
                .SetupGet(c => c.MessageContext)
                .Returns(ServiceMessageContext.Create(m_telemetry));
            channel
                .SetupGet(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            var configuration = new ApplicationConfiguration(m_telemetry)
            {
                ClientConfiguration = new ClientConfiguration()
            };

            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            });

            ISession session = factory.Create(channel.Object, configuration, endpoint);

            Assert.That(session, Is.Not.Null);
            Assert.That(session, Is.InstanceOf<Session>());
            Assert.That(session, Is.Not.InstanceOf<Opc.Ua.Client.ManagedSession>());

            session.Dispose();
        }

        [Test]
        public void DefaultSessionFactoryReturnDiagnosticsIsConfigurable()
        {
            var factory = new ManagedSessionFactory(m_telemetry);

            Assert.That(factory.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.None));

            factory.ReturnDiagnostics = DiagnosticsMasks.All;
            Assert.That(factory.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.All));

            factory.ReturnDiagnostics = DiagnosticsMasks.ServiceSymbolicId;
            Assert.That(factory.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.ServiceSymbolicId));
        }
    }
}
