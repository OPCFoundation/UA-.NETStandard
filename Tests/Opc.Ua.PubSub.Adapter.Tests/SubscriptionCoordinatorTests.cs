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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SubscriptionCoordinator"/>: affinity
    /// grouping, monitored item creation, priming and read-strategy lookup.
    /// </summary>
    [TestFixture]
    public sealed class SubscriptionCoordinatorTests
    {
        private static Mock<IServerSession> SessionReturningSubscriptions(
            List<FakeDataChangeSubscription> created)
        {
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
                        values[i] = new DataValue(new Variant(100 + i));
                    }
                    return new ValueTask<ArrayOf<DataValue>>(values.ToArrayOf());
                });
            return session;
        }

        [Test]
        public void ConstructorNullConfigurationThrows()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();

            Assert.That(
                () => new SubscriptionCoordinator(
                    null!,
                    session.Object,
                    SubscriptionAffinity.WriterGroup,
                    AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
        }

        [Test]
        public void ConstructorNullSessionThrows()
        {
            Assert.That(
                () => new SubscriptionCoordinator(
                    new PubSubConfigurationDataType(),
                    null!,
                    SubscriptionAffinity.WriterGroup,
                    AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("session"));
        }

        [Test]
        public async Task StartCreatesOneSubscriptionPerWriterGroup()
        {
            PublishedDataSetDataType pds1 = AdapterTestHelpers.PublishedDataSet(
                "PDS1", AdapterTestHelpers.Variable.Value(new NodeId(11u)));
            PublishedDataSetDataType pds2 = AdapterTestHelpers.PublishedDataSet(
                "PDS2", AdapterTestHelpers.Variable.Value(new NodeId(22u)));
            PubSubConfigurationDataType config = AdapterTestHelpers.Configuration(
                500, [pds1, pds2]);

            var created = new List<FakeDataChangeSubscription>();
            Mock<IServerSession> session = SessionReturningSubscriptions(created);
            await using var coordinator = new SubscriptionCoordinator(
                config,
                session.Object,
                SubscriptionAffinity.WriterGroup,
                AdapterTestHelpers.Telemetry());

            await coordinator.StartAsync().ConfigureAwait(false);

            Assert.That(created, Has.Count.EqualTo(1));
            Assert.That(created[0].MonitoredItems, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task StartCreatesOneSubscriptionPerWriterWhenAffinityIsDataSetWriter()
        {
            PublishedDataSetDataType pds1 = AdapterTestHelpers.PublishedDataSet(
                "PDS1", AdapterTestHelpers.Variable.Value(new NodeId(11u)));
            PublishedDataSetDataType pds2 = AdapterTestHelpers.PublishedDataSet(
                "PDS2", AdapterTestHelpers.Variable.Value(new NodeId(22u)));
            PubSubConfigurationDataType config = AdapterTestHelpers.Configuration(
                500, [pds1, pds2]);

            var created = new List<FakeDataChangeSubscription>();
            Mock<IServerSession> session = SessionReturningSubscriptions(created);
            await using var coordinator = new SubscriptionCoordinator(
                config,
                session.Object,
                SubscriptionAffinity.DataSetWriter,
                AdapterTestHelpers.Telemetry());

            await coordinator.StartAsync().ConfigureAwait(false);

            Assert.That(created, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task StartUsesSamplingHintWhenProvided()
        {
            PublishedDataSetDataType pds = AdapterTestHelpers.PublishedDataSet(
                "PDS",
                AdapterTestHelpers.Variable.Value(new NodeId(11u), 250),
                AdapterTestHelpers.Variable.Value(new NodeId(22u)));
            PubSubConfigurationDataType config = AdapterTestHelpers.Configuration(
                500, [pds]);

            var created = new List<FakeDataChangeSubscription>();
            Mock<IServerSession> session = SessionReturningSubscriptions(created);
            await using var coordinator = new SubscriptionCoordinator(
                config,
                session.Object,
                SubscriptionAffinity.WriterGroup,
                AdapterTestHelpers.Telemetry());

            await coordinator.StartAsync().ConfigureAwait(false);

            Assert.That(created[0].MonitoredItems[0].SamplingMs, Is.EqualTo(250));
            Assert.That(created[0].MonitoredItems[1].SamplingMs, Is.EqualTo(500));
        }

        [Test]
        public async Task StartPrimesReadStrategyCache()
        {
            var nodeId = new NodeId(11u);
            PublishedDataSetDataType pds = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(nodeId));
            PubSubConfigurationDataType config = AdapterTestHelpers.Configuration(
                500, [pds]);

            var created = new List<FakeDataChangeSubscription>();
            Mock<IServerSession> session = SessionReturningSubscriptions(created);
            await using var coordinator = new SubscriptionCoordinator(
                config,
                session.Object,
                SubscriptionAffinity.WriterGroup,
                AdapterTestHelpers.Telemetry());

            await coordinator.StartAsync().ConfigureAwait(false);
            IReadStrategy strategy = coordinator.GetReadStrategy("PDS");
            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            ];
            ArrayOf<DataValue> values = await strategy.ReadAsync(reads.ToArrayOf()).ConfigureAwait(false);

            Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(100)));
        }

        [Test]
        public async Task StartReflectsSubsequentDataChange()
        {
            var nodeId = new NodeId(11u);
            PublishedDataSetDataType pds = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(nodeId));
            PubSubConfigurationDataType config = AdapterTestHelpers.Configuration(
                500, [pds]);

            var created = new List<FakeDataChangeSubscription>();
            Mock<IServerSession> session = SessionReturningSubscriptions(created);
            await using var coordinator = new SubscriptionCoordinator(
                config,
                session.Object,
                SubscriptionAffinity.WriterGroup,
                AdapterTestHelpers.Telemetry());

            await coordinator.StartAsync().ConfigureAwait(false);
            FakeDataChangeSubscription subscription = created[0];
            (NodeId Node, uint Attribute, double Sampling) item = (
                created[0].MonitoredItems[0].NodeId,
                created[0].MonitoredItems[0].AttributeId,
                created[0].MonitoredItems[0].SamplingMs);
            subscription.Raise(1, item.Node, new DataValue(new Variant(777)));

            IReadStrategy strategy = coordinator.GetReadStrategy("PDS");
            ReadValueId[] reads =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            ];
            ArrayOf<DataValue> values = await strategy.ReadAsync(reads.ToArrayOf()).ConfigureAwait(false);

            Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(777)));
        }

        [Test]
        public async Task GetReadStrategyUnknownDataSetThrows()
        {
            PublishedDataSetDataType pds = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(new NodeId(11u)));
            PubSubConfigurationDataType config = AdapterTestHelpers.Configuration(
                500, [pds]);

            var created = new List<FakeDataChangeSubscription>();
            Mock<IServerSession> session = SessionReturningSubscriptions(created);
            await using var coordinator = new SubscriptionCoordinator(
                config,
                session.Object,
                SubscriptionAffinity.WriterGroup,
                AdapterTestHelpers.Telemetry());

            await coordinator.StartAsync().ConfigureAwait(false);

            Assert.That(
                () => coordinator.GetReadStrategy("Missing"),
                Throws.TypeOf<KeyNotFoundException>());
        }

        [Test]
        public async Task StartIsIdempotent()
        {
            PublishedDataSetDataType pds = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(new NodeId(11u)));
            PubSubConfigurationDataType config = AdapterTestHelpers.Configuration(
                500, [pds]);

            var created = new List<FakeDataChangeSubscription>();
            Mock<IServerSession> session = SessionReturningSubscriptions(created);
            await using var coordinator = new SubscriptionCoordinator(
                config,
                session.Object,
                SubscriptionAffinity.WriterGroup,
                AdapterTestHelpers.Telemetry());

            await coordinator.StartAsync().ConfigureAwait(false);
            await coordinator.StartAsync().ConfigureAwait(false);

            Assert.That(created, Has.Count.EqualTo(1));
        }
    }
}
