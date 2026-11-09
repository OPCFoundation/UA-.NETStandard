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
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Exercises the real subscription coordinator and publication source against an offline ranged source server.
    /// </summary>
    [TestFixture]
    public sealed class SubscriptionProjectionSecurityTests
    {
        /// <summary>
        /// Checks initial reads and later monitored values export exactly the authorized projection with its quality.
        /// </summary>
        [TestCase("1:2", new int[] { 20, 30 })]
        [TestCase("0", new int[] { 10 })]
        [TestCase("3", new int[] { 40 })]
        [TestCase("", new int[] { 10, 20, 30, 40 })]
        [Category("SecurityRegression")]
        public async Task SecurityF41SubscriptionPublishesOnlyRequestedRange(string indexRange, int[] expected)
        {
            var node = new NodeId(101u);
            DataValue complete = CompleteValue(10, 20, 30, 40);
            var server = new AdapterSecuritySession(new Dictionary<(NodeId, uint), DataValue>
            {
                [(node, Attributes.Value)] = complete
            });
            await using IServerSession session = server.Session.Object;
            PublishedDataSetDataType dataSet = AdapterSecuritySession.DataSet(
                "Projection",
                new PublishedVariableDataType
                {
                    PublishedVariable = node,
                    AttributeId = Attributes.Value,
                    IndexRange = indexRange
                });
            await using var coordinator = new SubscriptionCoordinator(
                AdapterTestHelpers.Configuration(500, [dataSet]), session,
                SubscriptionAffinity.WriterGroup, AdapterTestHelpers.Telemetry());
            await coordinator.StartAsync().ConfigureAwait(false);
            await using var metadata = new DataSetMetaDataBuilder(dataSet, session, AdapterTestHelpers.Telemetry());
            var source = new ServerPublishedDataSetSource(
                dataSet, coordinator.GetReadStrategy("Projection"), metadata, AdapterTestHelpers.Telemetry());

            PublishedDataSetSnapshot initial = await source.SampleAsync(source.BuildMetaData()).ConfigureAwait(false);

            Assert.That(server.Reads, Has.Count.EqualTo(1));
            Assert.That(server.Reads[0][0].IndexRange, Is.EqualTo(indexRange));
            Assert.That(server.Subscriptions[0].IndexRanges, Is.EqualTo([indexRange]));
            AssertProjection(initial.Fields[0], expected, complete);
            Assert.That(initial.Fields.Count, Is.EqualTo(1));

            DataValue update = CompleteValue(100, 200, 300, 400).WithStatus(StatusCodes.Good);
            server.Notify(node, Attributes.Value, update);
            PublishedDataSetSnapshot changed = await source.SampleAsync(source.BuildMetaData()).ConfigureAwait(false);
            var expectedUpdate = new int[expected.Length];
            for (int i = 0; i < expected.Length; i++)
            {
                expectedUpdate[i] = expected[i] * 10;
            }
            AssertProjection(changed.Fields[0], expectedUpdate, update);
            Assert.That(server.Reads, Has.Count.EqualTo(1), "Publication must only read the projection cache.");
        }

        /// <summary>
        /// Distinct ranges and the whole value have independent cache identities, and repeated ranges deduplicate.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF41DifferentRangesRemainIndependentAndNotificationsAreNotSlicedTwice()
        {
            var node = new NodeId(102u);
            var server = new AdapterSecuritySession(new Dictionary<(NodeId, uint), DataValue>
            {
                [(node, Attributes.Value)] = CompleteValue(10, 20, 30, 40)
            });
            await using IServerSession session = server.Session.Object;
            PublishedDataSetDataType dataSet = AdapterSecuritySession.DataSet(
                "Ranges",
                Variable(node, "0"), Variable(node, "2:3"), Variable(node, "0"), Variable(node, string.Empty));
            await using var coordinator = new SubscriptionCoordinator(
                AdapterTestHelpers.Configuration(500, [dataSet]), session,
                SubscriptionAffinity.WriterGroup, AdapterTestHelpers.Telemetry());
            await coordinator.StartAsync().ConfigureAwait(false);
            await using var metadata = new DataSetMetaDataBuilder(dataSet, session, AdapterTestHelpers.Telemetry());
            var source = new ServerPublishedDataSetSource(
                dataSet, coordinator.GetReadStrategy("Ranges"), metadata, AdapterTestHelpers.Telemetry());

            PublishedDataSetSnapshot initial = await source.SampleAsync(source.BuildMetaData()).ConfigureAwait(false);

            Assert.That(server.Subscriptions[0].MonitoredItems, Has.Count.EqualTo(3));
            Assert.That(server.Subscriptions[0].IndexRanges, Is.EqualTo(s_expectedRanges));
            Assert.That(server.Reads[0].Count, Is.EqualTo(3));
            Assert.That(server.Reads[0][1].IndexRange, Is.EqualTo("2:3"));
            Assert.That(initial.Fields[0].Value, Is.EqualTo(Variant.From(s_firstProjection)));
            Assert.That(initial.Fields[1].Value, Is.EqualTo(Variant.From(s_lastProjection)));
            Assert.That(initial.Fields[2].Value, Is.EqualTo(initial.Fields[0].Value));
            Assert.That(initial.Fields[3].Value, Is.EqualTo(Variant.From(s_completeSourceValues)));

            DataValue projected = CompleteValue(300, 400).WithStatus(StatusCodes.Good);
            server.Subscriptions[0].Raise(2, node, projected);
            PublishedDataSetSnapshot changed = await source.SampleAsync(source.BuildMetaData()).ConfigureAwait(false);
            AssertProjection(changed.Fields[1], [300, 400], projected);
            Assert.That(changed.Fields[0].Value, Is.EqualTo(initial.Fields[0].Value));
            Assert.That(changed.Fields[3].Value, Is.EqualTo(initial.Fields[3].Value));

            ArrayOf<DataValue> unregistered = await coordinator.GetReadStrategy("Ranges").ReadAsync(
                [new ReadValueId { NodeId = node, AttributeId = Attributes.Value, IndexRange = "1" }])
                .ConfigureAwait(false);
            Assert.That(unregistered[0].StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
            Assert.That(unregistered[0].WrappedValue.IsNull, Is.True);
        }

        /// <summary>
        /// An unsatisfied range must not turn into an authorized complete-value publication.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF41OutOfRangePublishesNoDataAndRetainsTimestamps()
        {
            var node = new NodeId(103u);
            DataValue complete = CompleteValue(10, 20, 30, 40);
            var server = new AdapterSecuritySession(new Dictionary<(NodeId, uint), DataValue>
            {
                [(node, Attributes.Value)] = complete
            });
            await using IServerSession session = server.Session.Object;
            PublishedDataSetDataType dataSet = AdapterSecuritySession.DataSet("OutOfRange", Variable(node, "8:9"));
            await using var coordinator = new SubscriptionCoordinator(
                AdapterTestHelpers.Configuration(500, [dataSet]), session,
                SubscriptionAffinity.WriterGroup, AdapterTestHelpers.Telemetry());
            await coordinator.StartAsync().ConfigureAwait(false);
            await using var metadata = new DataSetMetaDataBuilder(dataSet, session, AdapterTestHelpers.Telemetry());
            var source = new ServerPublishedDataSetSource(
                dataSet, coordinator.GetReadStrategy("OutOfRange"), metadata, AdapterTestHelpers.Telemetry());

            PublishedDataSetSnapshot snapshot = await source.SampleAsync(source.BuildMetaData()).ConfigureAwait(false);

            Assert.That(snapshot.Fields[0].Value.IsNull, Is.True);
            Assert.That(snapshot.Fields[0].StatusCode, Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(snapshot.Fields[0].SourceTimestamp, Is.EqualTo(complete.SourceTimestamp));
            Assert.That(snapshot.Fields[0].ServerTimestamp, Is.EqualTo(complete.ServerTimestamp));
            Assert.That(server.Reads[0][0].IndexRange, Is.EqualTo("8:9"));
        }

        /// <summary>
        /// Invalid syntax fails before opening a session or creating monitored items.
        /// </summary>
        [TestCase("2:1")]
        [TestCase("1:")]
        [TestCase("1:2,")]
        [Category("SecurityRegression")]
        public async Task SecurityF41MalformedRangeIsRejectedBeforeSessionUse(string range)
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            await using IServerSession sessionScope = session.Object;
            PublishedDataSetDataType dataSet = AdapterSecuritySession.DataSet(
                "Invalid", Variable(new NodeId(104u), range));

            Assert.That(
                () => new SubscriptionCoordinator(
                    AdapterTestHelpers.Configuration(500, [dataSet]), session.Object,
                    SubscriptionAffinity.WriterGroup, AdapterTestHelpers.Telemetry()),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadIndexRangeInvalid));
            session.Verify(s => s.ConnectAsync(It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(s => s.CreateDataChangeSubscriptionAsync(
                It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A legacy provider may monitor whole values but cannot silently ignore an unsupported projection.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF41UnsupportedProviderRejectsRangeBeforeMonitoringOrRead()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            await using IServerSession sessionScope = session.Object;
            var subscription = new Mock<IDataChangeSubscription>();
            session.Setup(s => s.CreateDataChangeSubscriptionAsync(
                    It.IsAny<double>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IDataChangeSubscription>(subscription.Object));
            PublishedDataSetDataType dataSet = AdapterSecuritySession.DataSet(
                "Unsupported", Variable(new NodeId(105u), "1:2"));
            await using var coordinator = new SubscriptionCoordinator(
                AdapterTestHelpers.Configuration(500, [dataSet]), session.Object,
                SubscriptionAffinity.WriterGroup, AdapterTestHelpers.Telemetry());

            await Assert.ThatAsync(
                async () => await coordinator.StartAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported)).ConfigureAwait(false);
            subscription.Verify(s => s.AddMonitoredItemAsync(
                It.IsAny<NodeId>(), It.IsAny<uint>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(s => s.ReadAsync(
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// The default provider passes the range into the actual managed monitored-item options.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF41ManagedSubscriptionForwardsRangeToMonitoredItem()
        {
            var item = new Mock<IMonitoredItem>();
            item.SetupGet(i => i.ClientHandle).Returns(42u);
            item.SetupGet(i => i.Created).Returns(true);
            IMonitoredItem? monitoredItem = item.Object;
            var items = new Mock<IMonitoredItemCollection>();
            items.Setup(i => i.TryAdd(
                    It.IsAny<string>(), It.IsAny<IOptionsMonitor<MonitoredItemOptions>>(), out monitoredItem))
                .Returns(true);
            var subscription = new Mock<ISubscription>();
            subscription.SetupGet(s => s.MonitoredItems).Returns(items.Object);
            subscription.SetupGet(s => s.Created).Returns(true);
            var manager = new Mock<ISubscriptionManager>();
            manager.Setup(m => m.Add(
                    It.IsAny<ISubscriptionNotificationHandler>(), It.IsAny<IOptionsMonitor<SubscriptionOptions>>()))
                .Returns(subscription.Object);
            await using var adapter = new DataChangeSubscription(manager.Object, 500, AdapterTestHelpers.Telemetry());

            uint handle = await adapter.AddMonitoredItemAsync(
                new NodeId(106u), Attributes.Value, "1:2", 500).ConfigureAwait(false);
            await adapter.ApplyChangesAsync().ConfigureAwait(false);

            Assert.That(handle, Is.EqualTo(42u));
            items.Verify(i => i.TryAdd(
                It.IsAny<string>(),
                It.Is<IOptionsMonitor<MonitoredItemOptions>>(o =>
                    o.CurrentValue.IndexRange == "1:2" &&
                    o.CurrentValue.TimestampsToReturn == TimestampsToReturn.Both),
                out monitoredItem), Times.Once);
        }

        /// <summary>
        /// Creates one ranged field definition.
        /// </summary>
        private static PublishedVariableDataType Variable(NodeId node, string range)
        {
            return new PublishedVariableDataType
            {
                PublishedVariable = node,
                AttributeId = Attributes.Value,
                IndexRange = range
            };
        }

        /// <summary>
        /// Creates a complete source array with nondefault quality and both timestamp components.
        /// </summary>
        private static DataValue CompleteValue(params int[] values)
        {
            return new DataValue(Variant.From(values.ToArrayOf()), StatusCodes.UncertainLastUsableValue)
                .WithSourceTimestamp(DateTimeUtc.From(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)))
                .WithServerTimestamp(DateTimeUtc.From(new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc)))
                .WithSourcePicoseconds(12)
                .WithServerPicoseconds(34);
        }

        /// <summary>
        /// Checks a publication contains only its selected array and preserves the associated quality and times.
        /// </summary>
        private static void AssertProjection(DataSetField field, int[] expected, in DataValue value)
        {
            Assert.That(field.Value.TryGetValue(out ArrayOf<int> actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected.ToArrayOf()));
            Assert.That(field.StatusCode, Is.EqualTo(value.StatusCode));
            Assert.That(field.SourceTimestamp, Is.EqualTo(value.SourceTimestamp));
            Assert.That(field.ServerTimestamp, Is.EqualTo(value.ServerTimestamp));
            Assert.That(field.SourcePicoSeconds, Is.EqualTo(value.SourcePicoseconds));
            Assert.That(field.ServerPicoSeconds, Is.EqualTo(value.ServerPicoseconds));
        }

        /// <summary>The independent projection identities expected on the server.</summary>
        private static readonly string[] s_expectedRanges = ["0", "2:3", string.Empty];

        /// <summary>The first authorized singleton projection.</summary>
        private static readonly ArrayOf<int> s_firstProjection = [10];

        /// <summary>The authorized final two elements.</summary>
        private static readonly ArrayOf<int> s_lastProjection = [30, 40];

        /// <summary>The complete source array, available only to the explicitly whole-value field.</summary>
        private static readonly ArrayOf<int> s_completeSourceValues = [10, 20, 30, 40];
    }
}
