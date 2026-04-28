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

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("ClassicSessionFactory")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ClassicSessionFactoryTests
    {
        private ITelemetryContext _telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            _telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ConstructorSetsTelemetry()
        {
            var factory = new ClassicSessionFactory(_telemetry);

            Assert.That(factory.Telemetry, Is.SameAs(_telemetry));
        }

        [Test]
        public void ReturnDiagnosticsDefaultIsNone()
        {
            var factory = new ClassicSessionFactory(_telemetry);

            Assert.That(factory.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.None));
        }

        [Test]
        public void ReturnDiagnosticsCanBeSet()
        {
            var factory = new ClassicSessionFactory(_telemetry)
            {
                ReturnDiagnostics = DiagnosticsMasks.All
            };

            Assert.That(factory.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.All));
        }

        [Test]
        public void CreateReturnsSessionWithCorrectEndpoint()
        {
            var factory = new ClassicSessionFactory(_telemetry);
            var channel = new Mock<ITransportChannel>();
            channel
                .SetupGet(c => c.MessageContext)
                .Returns(ServiceMessageContext.Create(_telemetry));
            channel
                .SetupGet(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            var configuration = new ApplicationConfiguration(_telemetry)
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

            session.Dispose();
        }

        [Test]
        public void CreateSetsReturnDiagnosticsOnSession()
        {
            var factory = new ClassicSessionFactory(_telemetry)
            {
                ReturnDiagnostics = DiagnosticsMasks.ServiceSymbolicId
            };

            var channel = new Mock<ITransportChannel>();
            channel
                .SetupGet(c => c.MessageContext)
                .Returns(ServiceMessageContext.Create(_telemetry));
            channel
                .SetupGet(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            var configuration = new ApplicationConfiguration(_telemetry)
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

            Assert.That(session.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.ServiceSymbolicId));

            session.Dispose();
        }

        [Test]
        public void RecreateAsyncThrowsWhenSessionIsNotSessionType()
        {
            var factory = new ClassicSessionFactory(_telemetry);
            var mockSession = new Mock<ISession>();

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(mockSession.Object).ConfigureAwait(false));
        }

        [Test]
        public void RecreateAsyncWithConnectionThrowsWhenSessionIsNotSessionType()
        {
            var factory = new ClassicSessionFactory(_telemetry);
            var mockSession = new Mock<ISession>();
            var mockConnection = new Mock<ITransportWaitingConnection>();

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(mockSession.Object, mockConnection.Object).ConfigureAwait(false));
        }

        [Test]
        public void RecreateAsyncWithChannelThrowsWhenSessionIsNotSessionType()
        {
            var factory = new ClassicSessionFactory(_telemetry);
            var mockSession = new Mock<ISession>();
            var mockChannel = new Mock<ITransportChannel>();

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(mockSession.Object, mockChannel.Object).ConfigureAwait(false));
        }

        [Test]
        public void CreateAsyncWithNullReverseConnectManagerForwardsToSimpleOverload()
        {
            var factory = new Mock<ClassicSessionFactory>(_telemetry) { CallBase = true };

            var configuration = new ApplicationConfiguration(_telemetry)
            {
                ClientConfiguration = new ClientConfiguration()
            };

            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            });

            using var identity = new UserIdentity();
            var mockSession = new Mock<ISession>();

            factory
                .Setup(f => f.CreateAsync(
                    configuration,
                    endpoint,
                    false,
                    false,
                    "TestSession",
                    30000u,
                    identity,
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSession.Object)
                .Verifiable();

            Task<ISession> task = factory.Object.CreateAsync(
                configuration,
                (ReverseConnectManager)null!,
                endpoint,
                false,
                false,
                "TestSession",
                30000u,
                identity,
                default,
                CancellationToken.None);

            Assert.DoesNotThrowAsync(async () => await task.ConfigureAwait(false));
            factory.Verify();
        }

        [Test]
        public void CreateWithAvailableEndpointsReturnsSession()
        {
            var factory = new ClassicSessionFactory(_telemetry);
            var channel = new Mock<ITransportChannel>();
            channel
                .SetupGet(c => c.MessageContext)
                .Returns(ServiceMessageContext.Create(_telemetry));
            channel
                .SetupGet(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);

            var configuration = new ApplicationConfiguration(_telemetry)
            {
                ClientConfiguration = new ClientConfiguration()
            };

            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            });

            ArrayOf<EndpointDescription> availableEndpoints =
            [
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://localhost:4840",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                }
            ];

            ArrayOf<string> discoveryUris = ["urn:test"];

            ISession session = factory.Create(
                channel.Object,
                configuration,
                endpoint,
                null,
                null,
                availableEndpoints,
                discoveryUris);

            Assert.That(session, Is.Not.Null);
            Assert.That(session, Is.InstanceOf<Session>());

            session.Dispose();
        }

        [Test]
        public void RecreateAsyncThrowsWithCorrectParameterName()
        {
            var factory = new ClassicSessionFactory(_telemetry);
            var mockSession = new Mock<ISession>();

            ArgumentException ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(mockSession.Object).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("sessionTemplate"));
        }

        [Test]
        public void RecreateAsyncWithConnectionThrowsWithCorrectParameterName()
        {
            var factory = new ClassicSessionFactory(_telemetry);
            var mockSession = new Mock<ISession>();
            var mockConnection = new Mock<ITransportWaitingConnection>();

            ArgumentException ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(mockSession.Object, mockConnection.Object).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("sessionTemplate"));
        }

        [Test]
        public void RecreateAsyncWithChannelThrowsWithCorrectParameterName()
        {
            var factory = new ClassicSessionFactory(_telemetry);
            var mockSession = new Mock<ISession>();
            var mockChannel = new Mock<ITransportChannel>();

            ArgumentException ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(mockSession.Object, mockChannel.Object).ConfigureAwait(false));

            Assert.That(ex!.ParamName, Is.EqualTo("sessionTemplate"));
        }

        [Test]
        public void TelemetryCanBeSetViaInitializer()
        {
            var factory = new ClassicSessionFactory(_telemetry)
            {
                Telemetry = _telemetry
            };

            Assert.That(factory.Telemetry, Is.SameAs(_telemetry));
        }

        [Test]
        public void CreateAsyncOverloadWithConnectionForwardsThroughChain()
        {
            var factory = new Mock<ClassicSessionFactory>(_telemetry) { CallBase = true };

            var configuration = new ApplicationConfiguration(_telemetry)
            {
                ClientConfiguration = new ClientConfiguration()
            };

            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            });

            var mockSession = new Mock<ISession>();
            var mockConnection = new Mock<ITransportWaitingConnection>();

            factory
                .Setup(f => f.CreateAsync(
                    configuration,
                    mockConnection.Object,
                    endpoint,
                    true,
                    false,
                    "Test",
                    5000u,
                    It.IsAny<IUserIdentity>(),
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockSession.Object)
                .Verifiable();

            Task<ISession> task = factory.Object.CreateAsync(
                configuration,
                mockConnection.Object,
                endpoint,
                true,
                false,
                "Test",
                5000u,
                null,
                default,
                CancellationToken.None);

            Assert.DoesNotThrowAsync(async () => await task.ConfigureAwait(false));
            factory.Verify();
        }
    }
}
