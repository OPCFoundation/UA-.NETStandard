/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.DependencyInjection;
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.PubSub.Adapter.Tests.Unit
{
    /// <summary>
    /// Unit tests for the dependency-injection runtime types:
    /// <see cref="ServerSessionFactory"/>,
    /// <see cref="ServerAdapterRuntime"/> and
    /// <see cref="ServerAdapterHostedService"/>.
    /// </summary>
    [TestFixture]
    public sealed class ServerAdapterRuntimeTests
    {
        private static SubscriptionCoordinator CreateCoordinator(
            List<FakeDataChangeSubscription> created)
        {
            PublishedDataSetDataType pds = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(new NodeId(11u)));
            PubSubConfigurationDataType config = AdapterTestHelpers.Configuration(
                500, new[] { pds });

            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session
                .Setup(s => s.CreateDataChangeSubscriptionAsync(
                    It.IsAny<double>(), It.IsAny<CancellationToken>()))
                .Returns((double interval, CancellationToken ct) =>
                {
                    var sub = new FakeDataChangeSubscription();
                    created.Add(sub);
                    return new ValueTask<IDataChangeSubscription>(sub);
                });
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<ReadValueId> reads, CancellationToken ct) =>
                {
                    var values = new DataValue[reads.Count];
                    for (int i = 0; i < reads.Count; i++)
                    {
                        values[i] = new DataValue(new Variant(i));
                    }
                    return new ValueTask<ArrayOf<DataValue>>(values.ToArrayOf());
                });

            return new SubscriptionCoordinator(
                config,
                session.Object,
                SubscriptionAffinity.WriterGroup,
                AdapterTestHelpers.Telemetry());
        }

        [Test]
        public void FactoryCreateNullOptionsThrows()
        {
            var factory = new ServerSessionFactory();

            Assert.That(
                () => factory.Create(null!, AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("options"));
        }

        [Test]
        public void FactoryCreateNullTelemetryThrows()
        {
            var factory = new ServerSessionFactory();

            Assert.That(
                () => factory.Create(
                    new ServerConnectionOptions { EndpointUrl = "opc.tcp://host:4840" },
                    null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public async Task FactoryCreateReturnsExternalServerSessionAsync()
        {
            var factory = new ServerSessionFactory();

            IServerSession session = factory.Create(
                new ServerConnectionOptions { EndpointUrl = "opc.tcp://host:4840" },
                AdapterTestHelpers.Telemetry());

            Assert.That(session, Is.InstanceOf<ServerSession>());
            Assert.That(session.IsConnected, Is.False);
            await session.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public void RuntimeAddSessionNullThrows()
        {
            var runtime = new ServerAdapterRuntime();

            Assert.That(
                () => runtime.AddSession(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("session"));
        }

        [Test]
        public void RuntimeAddCoordinatorNullThrows()
        {
            var runtime = new ServerAdapterRuntime();

            Assert.That(
                () => runtime.AddCoordinator(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("coordinator"));
        }

        [Test]
        public async Task RuntimeStartStartsRegisteredCoordinatorsAsync()
        {
            var created = new List<FakeDataChangeSubscription>();
            SubscriptionCoordinator coordinator = CreateCoordinator(created);
            var runtime = new ServerAdapterRuntime();
            runtime.AddCoordinator(coordinator);

            await runtime.StartAsync().ConfigureAwait(false);

            Assert.That(created, Has.Count.EqualTo(1));

            await runtime.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task RuntimeStartIsIdempotentAsync()
        {
            var created = new List<FakeDataChangeSubscription>();
            SubscriptionCoordinator coordinator = CreateCoordinator(created);
            var runtime = new ServerAdapterRuntime();
            runtime.AddCoordinator(coordinator);

            await runtime.StartAsync().ConfigureAwait(false);
            await runtime.StartAsync().ConfigureAwait(false);

            Assert.That(created, Has.Count.EqualTo(1));

            await runtime.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task RuntimeDisposeDisposesSessionsAsync()
        {
            var session = new Mock<IServerSession>();
            session.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));
            var runtime = new ServerAdapterRuntime();
            runtime.AddSession(session.Object);

            await runtime.DisposeAsync().ConfigureAwait(false);

            session.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Test]
        public async Task RuntimeAddSessionAfterDisposeThrowsAsync()
        {
            var runtime = new ServerAdapterRuntime();
            await runtime.DisposeAsync().ConfigureAwait(false);

            var session = new Mock<IServerSession>();
            Assert.That(
                () => runtime.AddSession(session.Object),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task RuntimeDisposeIsIdempotentAsync()
        {
            var session = new Mock<IServerSession>();
            session.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));
            var runtime = new ServerAdapterRuntime();
            runtime.AddSession(session.Object);

            await runtime.DisposeAsync().ConfigureAwait(false);
            await runtime.DisposeAsync().ConfigureAwait(false);

            session.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Test]
        public void HostedServiceNullApplicationThrows()
        {
            var runtime = new ServerAdapterRuntime();

            Assert.That(
                () => new ServerAdapterHostedService(null!, runtime),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("application"));
        }

        [Test]
        public void HostedServiceNullRuntimeThrows()
        {
            var application = new Mock<IPubSubApplication>().Object;

            Assert.That(
                () => new ServerAdapterHostedService(application, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("runtime"));
        }

        [Test]
        public async Task HostedServiceStartStartsCoordinatorsAndStopDisposesAsync()
        {
            var created = new List<FakeDataChangeSubscription>();
            SubscriptionCoordinator coordinator = CreateCoordinator(created);
            var session = new Mock<IServerSession>();
            session.Setup(s => s.DisposeAsync()).Returns(default(ValueTask));
            var runtime = new ServerAdapterRuntime();
            runtime.AddCoordinator(coordinator);
            runtime.AddSession(session.Object);
            var hosted = new ServerAdapterHostedService(
                new Mock<IPubSubApplication>().Object, runtime);

            await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(created, Has.Count.EqualTo(1));

            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
            session.Verify(s => s.DisposeAsync(), Times.Once);
        }
    }
}
