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
    /// <summary>
    /// Deterministic unit tests for <see cref="ChannelManagerSessionFactory"/> that exercise
    /// its constructor guards, diagnostics delegation, argument validation and the
    /// channel-only reconnect participant without opening a real session or touching the network.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ChannelManagerSessionFactory")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ChannelManagerSessionFactoryTests
    {
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ConstructorWithNullManagerThrowsArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
                new ChannelManagerSessionFactory(null, m_telemetry));

            Assert.That(ex.ParamName, Is.EqualTo("manager"));
        }

        [Test]
        public void ConstructorWithNullTelemetryThrowsArgumentNullException()
        {
            var manager = new Mock<IClientChannelManager>();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
                new ChannelManagerSessionFactory(manager.Object, null));

            Assert.That(ex.ParamName, Is.EqualTo("telemetry"));
        }

        [Test]
        public void ConstructorStoresTelemetry()
        {
            var manager = new Mock<IClientChannelManager>();

            var factory = new ChannelManagerSessionFactory(manager.Object, m_telemetry);

            Assert.That(factory.Telemetry, Is.SameAs(m_telemetry));
        }

        [Test]
        public void ReturnDiagnosticsDefaultsToNone()
        {
            var manager = new Mock<IClientChannelManager>();

            var factory = new ChannelManagerSessionFactory(manager.Object, m_telemetry);

            Assert.That(factory.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.None));
        }

        [Test]
        public void ConstructorAppliesReturnDiagnostics()
        {
            var manager = new Mock<IClientChannelManager>();

            var factory = new ChannelManagerSessionFactory(
                manager.Object,
                m_telemetry,
                DiagnosticsMasks.All);

            Assert.That(factory.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.All));
        }

        [Test]
        public void ReturnDiagnosticsSetterUpdatesValue()
        {
            var manager = new Mock<IClientChannelManager>();

            var factory = new ChannelManagerSessionFactory(manager.Object, m_telemetry)
            {
                ReturnDiagnostics = DiagnosticsMasks.ServiceSymbolicId
            };

            Assert.That(factory.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.ServiceSymbolicId));

            factory.ReturnDiagnostics = DiagnosticsMasks.All;

            Assert.That(factory.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.All));
        }

        [Test]
        public void ConstructorAcceptsTimeProvider()
        {
            var manager = new Mock<IClientChannelManager>();

            var factory = new ChannelManagerSessionFactory(
                manager.Object,
                m_telemetry,
                DiagnosticsMasks.SymbolicId,
                TimeProvider.System);

            Assert.That(factory.Telemetry, Is.SameAs(m_telemetry));
            Assert.That(factory.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.SymbolicId));
        }

        [Test]
        public async Task CreateChannelAsyncReturnsChannelFromManagerAsync()
        {
            var channel = new Mock<IManagedTransportChannel>();
            var manager = new Mock<IClientChannelManager>();
            manager
                .Setup(m => m.GetAsync(
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<Func<IManagedTransportChannel, IReconnectParticipant>>(),
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IManagedTransportChannel>(channel.Object));

            var factory = new ChannelManagerSessionFactory(manager.Object, m_telemetry);
            ConfiguredEndpoint endpoint = CreateEndpoint();

            ITransportChannel result = await factory.CreateChannelAsync(
                CreateConfiguration(),
                null,
                endpoint,
                updateBeforeConnect: false,
                checkDomain: false,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.SameAs(channel.Object));
            Assert.That(endpoint.UpdateBeforeConnect, Is.False);
            Assert.That(endpoint.Configuration, Is.Not.Null);
            manager.Verify(
                m => m.GetAsync(
                    endpoint,
                    It.IsAny<Func<IManagedTransportChannel, IReconnectParticipant>>(),
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task CreateChannelAsyncWithConnectionKeepsUpdateBeforeConnectAsync()
        {
            var channel = new Mock<IManagedTransportChannel>();
            var connection = new Mock<ITransportWaitingConnection>();
            var manager = new Mock<IClientChannelManager>();
            manager
                .Setup(m => m.GetAsync(
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<Func<IManagedTransportChannel, IReconnectParticipant>>(),
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IManagedTransportChannel>(channel.Object));

            var factory = new ChannelManagerSessionFactory(manager.Object, m_telemetry);
            ConfiguredEndpoint endpoint = CreateEndpoint();

            ITransportChannel result = await factory.CreateChannelAsync(
                CreateConfiguration(),
                connection.Object,
                endpoint,
                updateBeforeConnect: true,
                checkDomain: false,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Is.SameAs(channel.Object));
            Assert.That(endpoint.UpdateBeforeConnect, Is.True);
        }

        [Test]
        public void CreateChannelAsyncWithNullConfigurationThrowsArgumentNullException()
        {
            var manager = new Mock<IClientChannelManager>();
            var factory = new ChannelManagerSessionFactory(manager.Object, m_telemetry);
            ConfiguredEndpoint endpoint = CreateEndpoint();

            ArgumentNullException ex = Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await factory.CreateChannelAsync(
                    null,
                    null,
                    endpoint,
                    updateBeforeConnect: false,
                    checkDomain: false,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.ParamName, Is.EqualTo("configuration"));
        }

        [Test]
        public void CreateChannelAsyncWithNullEndpointThrowsArgumentNullException()
        {
            var manager = new Mock<IClientChannelManager>();
            var factory = new ChannelManagerSessionFactory(manager.Object, m_telemetry);

            ArgumentNullException ex = Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await factory.CreateChannelAsync(
                    CreateConfiguration(),
                    null,
                    null,
                    updateBeforeConnect: false,
                    checkDomain: false,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.ParamName, Is.EqualTo("endpoint"));
        }

        [Test]
        public void CreateAsyncReverseConnectWithNullEndpointThrowsArgumentNullException()
        {
            var manager = new Mock<IClientChannelManager>();
            var factory = new ChannelManagerSessionFactory(manager.Object, m_telemetry);

            ArgumentNullException ex = Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await factory.CreateAsync(
                    CreateConfiguration(),
                    (ReverseConnectManager)null,
                    null,
                    updateBeforeConnect: false,
                    checkDomain: false,
                    "TestSession",
                    30000u,
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.ParamName, Is.EqualTo("endpoint"));
        }

        [Test]
        public void CreateAsyncReverseConnectWithNullManagerDelegatesToPlainPath()
        {
            var manager = new Mock<IClientChannelManager>();
            var factory = new ChannelManagerSessionFactory(manager.Object, m_telemetry);

            ArgumentNullException ex = Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await factory.CreateAsync(
                    null,
                    (ReverseConnectManager)null,
                    CreateEndpoint(),
                    updateBeforeConnect: false,
                    checkDomain: false,
                    "TestSession",
                    30000u,
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.ParamName, Is.EqualTo("configuration"));
        }

        [Test]
        public void RecreateAsyncWithNullTemplateDelegatesToInnerFactory()
        {
            var manager = new Mock<IClientChannelManager>();
            var factory = new ChannelManagerSessionFactory(manager.Object, m_telemetry);

            ArgumentException ex = Assert.ThrowsAsync<ArgumentException>(async () =>
                await factory.RecreateAsync(null, CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.ParamName, Is.EqualTo("sessionTemplate"));
        }

        [Test]
        public async Task CreateChannelAsyncBindsChannelOnlyReconnectParticipantAsync()
        {
            var channel = new Mock<IManagedTransportChannel>();
            var manager = new Mock<IClientChannelManager>();
            IReconnectParticipant capturedParticipant = null;
            manager
                .Setup(m => m.GetAsync(
                    It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<Func<IManagedTransportChannel, IReconnectParticipant>>(),
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    ConfiguredEndpoint endpoint,
                    Func<IManagedTransportChannel, IReconnectParticipant> participantFactory,
                    ITransportWaitingConnection connection,
                    CancellationToken ct) =>
                {
                    capturedParticipant = participantFactory(channel.Object);
                    return new ValueTask<IManagedTransportChannel>(channel.Object);
                });

            var factory = new ChannelManagerSessionFactory(manager.Object, m_telemetry);
            ConfiguredEndpoint configuredEndpoint = CreateEndpoint();

            await factory.CreateChannelAsync(
                CreateConfiguration(),
                null,
                configuredEndpoint,
                updateBeforeConnect: false,
                checkDomain: false,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(capturedParticipant, Is.Not.Null);
            Assert.That(capturedParticipant.Id, Does.StartWith("ChannelManagerSessionFactory-"));
            Assert.That(capturedParticipant.Endpoint, Is.SameAs(configuredEndpoint));

            ParticipantReconnectResult reconnectResult = await capturedParticipant
                .OnReconnectAsync(channel.Object, 0, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(reconnectResult, Is.EqualTo(ParticipantReconnectResult.Reactivated));
        }

        private static ConfiguredEndpoint CreateEndpoint()
        {
            return new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            });
        }

        private ApplicationConfiguration CreateConfiguration()
        {
            return new ApplicationConfiguration(m_telemetry)
            {
                ClientConfiguration = new ClientConfiguration()
            };
        }
    }
}
