/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Unit tests for <see cref="OpenUsdConnectorFactory"/>.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorFactoryTests
    {
        private static Mock<ITelemetryContext> Telemetry()
        {
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(t => t.LoggerFactory).Returns(NullLoggerFactory.Instance);
            return telemetry;
        }

        private static Mock<ISession> Session()
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            return session;
        }

        [Test]
        public void CtorNullTelemetryThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new OpenUsdConnectorFactory(null!));
        }

        [Test]
        public void CtorNullDefaultOptionsThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => new OpenUsdConnectorFactory(Telemetry().Object, null!));
        }

        [Test]
        public void CreateReturnsConnector()
        {
            var factory = new OpenUsdConnectorFactory(Telemetry().Object);

            OpenUsdConnector connector = factory.Create(Session().Object, new MockUsdSink());

            Assert.That(connector, Is.Not.Null);
        }

        [Test]
        public void CreateNullSessionThrows()
        {
            var factory = new OpenUsdConnectorFactory(Telemetry().Object);

            Assert.Throws<ArgumentNullException>(() => factory.Create(null!, new MockUsdSink()));
        }
    }

    /// <summary>
    /// Unit tests for <see cref="OpenUsdConnector"/> construction and fail-closed
    /// command actuation that do not require a live server.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorConstructionTests
    {
        private static Mock<ISession> Session()
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            return session;
        }

        [Test]
        public void CtorNullSessionThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new OpenUsdConnector(null!, new MockUsdSink()));
        }

        [Test]
        public void CtorNullSinkThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new OpenUsdConnector(Session().Object, null!));
        }

        [Test]
        public async Task IssueCommandIsFailClosedByDefault()
        {
            var connector = new OpenUsdConnector(Session().Object, new MockUsdSink());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => connector.IssueCommandAsync(1.0, CancellationToken.None));
        }
    }

    /// <summary>
    /// Unit tests for the <c>AddOpenUsdConnector(...)</c> dependency-injection extensions.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorDiTests
    {
        [Test]
        public void AddOpenUsdConnectorRegistersFactoryAndOptions()
        {
            var services = new ServiceCollection();
            services.AddOpenUsdConnector(o => o.EnableCommands = true);

            using ServiceProvider provider = services.BuildServiceProvider();

            var factory = provider.GetService<OpenUsdConnectorFactory>();
            var options = provider.GetService<OpenUsdConnectorOptions>();

            Assert.That(factory, Is.Not.Null);
            Assert.That(options, Is.Not.Null);
            Assert.That(options!.EnableCommands, Is.True);
        }

        [Test]
        public void AddOpenUsdConnectorNullServicesThrows()
        {
            IServiceCollection services = null!;

            Assert.Throws<ArgumentNullException>(() => services.AddOpenUsdConnector());
        }

        [Test]
        public void AddOpenUsdConnectorOnOpcUaBuilderRegistersFactory()
        {
            IOpcUaBuilder builder = new ServiceCollection().AddOpcUa();

            using ServiceProvider provider = builder.AddOpenUsdConnector().Services.BuildServiceProvider();

            Assert.That(provider.GetService<OpenUsdConnectorFactory>(), Is.Not.Null);
        }

        [Test]
        public void AddOpenUsdConnectorNullBuilderThrows()
        {
            IOpcUaBuilder builder = null!;

            Assert.Throws<ArgumentNullException>(() => builder.AddOpenUsdConnector());
        }

        [Test]
        public void AddOpenUsdConnectorNullClientBuilderThrows()
        {
            IOpcUaClientBuilder builder = null!;

            Assert.Throws<ArgumentNullException>(() => builder.AddOpenUsdConnector());
        }
    }
}
