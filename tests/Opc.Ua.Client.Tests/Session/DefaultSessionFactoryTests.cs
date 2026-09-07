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
using Microsoft.Extensions.Logging;
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
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ConstructorSetsTelemetry()
        {
            var factory = new DefaultSessionFactory(m_telemetry);

            Assert.That(factory.Telemetry, Is.SameAs(m_telemetry));
        }

        [Test]
        public void ReturnDiagnosticsDefaultIsNone()
        {
            var factory = new DefaultSessionFactory(m_telemetry);

            Assert.That(factory.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.None));
        }

        [Test]
        public void ReturnDiagnosticsCanBeSet()
        {
            var factory = new DefaultSessionFactory(m_telemetry)
            {
                ReturnDiagnostics = DiagnosticsMasks.All
            };

            Assert.That(factory.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.All));
        }

        [Test]
        public void CreateReturnsSessionWithCorrectEndpoint()
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

            session.Dispose();
        }

        [Test]
        public void CreateSetsReturnDiagnosticsOnSession()
        {
            var factory = new DefaultSessionFactory(m_telemetry)
            {
                ReturnDiagnostics = DiagnosticsMasks.ServiceSymbolicId
            };

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

            Assert.That(session.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.ServiceSymbolicId));

            session.Dispose();
        }

        [Test]
        public void RecreateAsyncThrowsWhenSessionIsNotSessionType()
        {
            var factory = new DefaultSessionFactory(m_telemetry);
            var mockSession = new Mock<ISession>();

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(mockSession.Object).ConfigureAwait(false));
        }

        [Test]
        public void RecreateAsyncWithConnectionThrowsWhenSessionIsNotSessionType()
        {
            var factory = new DefaultSessionFactory(m_telemetry);
            var mockSession = new Mock<ISession>();
            var mockConnection = new Mock<ITransportWaitingConnection>();

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(mockSession.Object, mockConnection.Object).ConfigureAwait(false));
        }

        [Test]
        public void RecreateAsyncWithChannelThrowsWhenSessionIsNotSessionType()
        {
            var factory = new DefaultSessionFactory(m_telemetry);
            var mockSession = new Mock<ISession>();
            var mockChannel = new Mock<ITransportChannel>();

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(mockSession.Object, mockChannel.Object).ConfigureAwait(false));
        }

        [Test]
        public void CreateAsyncWithNullReverseConnectManagerForwardsToSimpleOverload()
        {
            var factory = new Mock<DefaultSessionFactory>(m_telemetry) { CallBase = true };

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

            var identity = new UserIdentity();
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
                (ReverseConnectManager)null,
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
            var factory = new DefaultSessionFactory(m_telemetry);
            var mockSession = new Mock<ISession>();

            ArgumentException ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(mockSession.Object).ConfigureAwait(false));

            Assert.That(ex.ParamName, Is.EqualTo("sessionTemplate"));
        }

        [Test]
        public void RecreateAsyncWithConnectionThrowsWithCorrectParameterName()
        {
            var factory = new DefaultSessionFactory(m_telemetry);
            var mockSession = new Mock<ISession>();
            var mockConnection = new Mock<ITransportWaitingConnection>();

            ArgumentException ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(mockSession.Object, mockConnection.Object).ConfigureAwait(false));

            Assert.That(ex.ParamName, Is.EqualTo("sessionTemplate"));
        }

        [Test]
        public void RecreateAsyncWithChannelThrowsWithCorrectParameterName()
        {
            var factory = new DefaultSessionFactory(m_telemetry);
            var mockSession = new Mock<ISession>();
            var mockChannel = new Mock<ITransportChannel>();

            ArgumentException ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(mockSession.Object, mockChannel.Object).ConfigureAwait(false));

            Assert.That(ex.ParamName, Is.EqualTo("sessionTemplate"));
        }

        [Test]
        public void TelemetryCanBeSetViaInitializer()
        {
            var factory = new DefaultSessionFactory(m_telemetry)
            {
                Telemetry = m_telemetry
            };

            Assert.That(factory.Telemetry, Is.SameAs(m_telemetry));
        }

        [Test]
        public void CreateAsyncOverloadWithConnectionForwardsThroughChain()
        {
            var factory = new Mock<DefaultSessionFactory>(m_telemetry) { CallBase = true };

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

        private static readonly Uri s_reverseEndpointUrl = new("opc.tcp://localhost:4840");

        [Test]
        public async Task ReverseConnectRetryReturnsFirstSessionWhenConnectionIsHealthyAsync()
        {
            ITransportWaitingConnection connection = new Mock<ITransportWaitingConnection>().Object;
            ISession session = new Mock<ISession>().Object;
            int resolveCalls = 0;

            ISession result = await DefaultSessionFactory
                .CreateReverseConnectSessionWithRetryAsync(
                    connection,
                    (conn, ct) => Task.FromResult(session),
                    ct =>
                    {
                        resolveCalls++;
                        return Task.FromResult(connection);
                    },
                    maxAttempts: 3,
                    logger: null,
                    endpointUrl: s_reverseEndpointUrl,
                    ct: CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result, Is.SameAs(session));
            Assert.That(resolveCalls, Is.Zero, "a healthy connection must not be re-fetched");
        }

        [Test]
        public async Task ReverseConnectRetryRefetchesConnectionAfterStaleFailureAsync()
        {
            ITransportWaitingConnection firstConnection = new Mock<ITransportWaitingConnection>().Object;
            ITransportWaitingConnection secondConnection = new Mock<ITransportWaitingConnection>().Object;
            ISession session = new Mock<ISession>().Object;
            int createCalls = 0;
            int resolveCalls = 0;

            ISession result = await DefaultSessionFactory
                .CreateReverseConnectSessionWithRetryAsync(
                    firstConnection,
                    (conn, ct) =>
                    {
                        createCalls++;
                        if (createCalls == 1)
                        {
                            Assert.That(conn, Is.SameAs(firstConnection));
                            throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                        }

                        Assert.That(conn, Is.SameAs(secondConnection),
                            "the retry must use the freshly delivered connection");
                        return Task.FromResult(session);
                    },
                    ct =>
                    {
                        resolveCalls++;
                        return Task.FromResult(secondConnection);
                    },
                    maxAttempts: 3,
                    // Exercise the warning-log branch with a real logger.
                    logger: m_telemetry.CreateLogger<DefaultSessionFactory>(),
                    endpointUrl: s_reverseEndpointUrl,
                    ct: CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result, Is.SameAs(session));
            Assert.That(createCalls, Is.EqualTo(2));
            Assert.That(resolveCalls, Is.EqualTo(1));
        }

        [Test]
        public void ReverseConnectRetryPropagatesNonTransientFailureWithoutRefetch()
        {
            ITransportWaitingConnection connection = new Mock<ITransportWaitingConnection>().Object;
            int resolveCalls = 0;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await DefaultSessionFactory
                    .CreateReverseConnectSessionWithRetryAsync(
                        connection,
                        (conn, ct) =>
                            throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected),
                        ct =>
                        {
                            resolveCalls++;
                            return Task.FromResult(connection);
                        },
                        maxAttempts: 3,
                        logger: null,
                        endpointUrl: s_reverseEndpointUrl,
                        ct: CancellationToken.None)
                    .ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadIdentityTokenRejected));
            Assert.That(resolveCalls, Is.Zero,
                "a non-transient failure must not trigger a reverse-connection re-fetch");
        }

        [Test]
        public void ReverseConnectRetryStopsAfterMaxAttemptsAndSurfacesStaleFailure()
        {
            ITransportWaitingConnection connection = new Mock<ITransportWaitingConnection>().Object;
            int createCalls = 0;
            int resolveCalls = 0;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await DefaultSessionFactory
                    .CreateReverseConnectSessionWithRetryAsync(
                        connection,
                        (conn, ct) =>
                        {
                            createCalls++;
                            throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                        },
                        ct =>
                        {
                            resolveCalls++;
                            return Task.FromResult(connection);
                        },
                        maxAttempts: 3,
                        logger: null,
                        endpointUrl: s_reverseEndpointUrl,
                        ct: CancellationToken.None)
                    .ConfigureAwait(false))!;

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
            Assert.That(createCalls, Is.EqualTo(3), "initial attempt plus two bounded retries");
            Assert.That(resolveCalls, Is.EqualTo(2), "a fresh connection is fetched before each retry");
        }
    }
}
