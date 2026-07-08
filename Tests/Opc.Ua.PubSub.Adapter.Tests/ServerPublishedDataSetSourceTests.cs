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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ServerPublishedDataSetSource"/>: building
    /// read requests from published variables, mapping values to DataSet fields,
    /// fail-soft gap handling and metadata delegation.
    /// </summary>
    [TestFixture]
    public sealed class ServerPublishedDataSetSourceTests
    {
        private sealed class RecordingReadStrategy : IReadStrategy
        {
            private readonly ArrayOf<DataValue> m_values;

            public RecordingReadStrategy(ArrayOf<DataValue> values)
            {
                m_values = values;
            }

            public ArrayOf<ReadValueId> LastRead { get; private set; } = ArrayOf<ReadValueId>.Null;

            public ValueTask<ArrayOf<DataValue>> ReadAsync(
                ArrayOf<ReadValueId> nodesToRead,
                CancellationToken cancellationToken = default)
            {
                LastRead = nodesToRead;
                return new ValueTask<ArrayOf<DataValue>>(m_values);
            }
        }

        private static IDataSetMetaDataBuilder MetaDataBuilder()
        {
            var mock = new Mock<IDataSetMetaDataBuilder>();
            mock.Setup(b => b.BuildMetaData()).Returns(new DataSetMetaDataType { Name = "Meta" });
            mock.Setup(b => b.ResolveAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DataSetMetaDataType>(new DataSetMetaDataType()));
            return mock.Object;
        }

        private static DataSetMetaDataType MetaWithFields(params string[] names)
        {
            var fields = new FieldMetaData[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                fields[i] = new FieldMetaData { Name = names[i] };
            }
            return new DataSetMetaDataType { Fields = fields.ToArrayOf() };
        }

        [Test]
        public void ConstructorNullConfigurationThrows()
        {
            var strategy = new RecordingReadStrategy(ArrayOf<DataValue>.Empty);

            Assert.That(
                () => new ServerPublishedDataSetSource(
                    null!, strategy, MetaDataBuilder(), AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
        }

        [Test]
        public void ConstructorNullStrategyThrows()
        {
            Assert.That(
                () => new ServerPublishedDataSetSource(
                    new PublishedDataSetDataType(), null!, MetaDataBuilder(),
                    AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("strategy"));
        }

        [Test]
        public void ConstructorNullMetaDataBuilderThrows()
        {
            var strategy = new RecordingReadStrategy(ArrayOf<DataValue>.Empty);

            Assert.That(
                () => new ServerPublishedDataSetSource(
                    new PublishedDataSetDataType(), strategy, null!,
                    AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("metaDataBuilder"));
        }

        [Test]
        public void BuildMetaDataDelegatesToBuilder()
        {
            var builder = new Mock<IDataSetMetaDataBuilder>();
            builder.Setup(b => b.BuildMetaData())
                .Returns(new DataSetMetaDataType { Name = "Delegated" });
            var source = new ServerPublishedDataSetSource(
                new PublishedDataSetDataType(),
                new RecordingReadStrategy(ArrayOf<DataValue>.Empty),
                builder.Object,
                AdapterTestHelpers.Telemetry());

            DataSetMetaDataType metaData = source.BuildMetaData();

            Assert.That(metaData.Name, Is.EqualTo("Delegated"));
            builder.Verify(b => b.BuildMetaData(), Times.Once);
        }

        [Test]
        public async Task SampleBuildsReadValueIdsFromPublishedVariables()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS",
                AdapterTestHelpers.Variable.Value(new NodeId(11u)),
                AdapterTestHelpers.Variable.Value(new NodeId(22u)));
            var strategy = new RecordingReadStrategy(new[]
            {
                new DataValue(new Variant(1)),
                new DataValue(new Variant(2))
            }.ToArrayOf());
            var source = new ServerPublishedDataSetSource(
                config, strategy, MetaDataBuilder(), AdapterTestHelpers.Telemetry());

            await source.SampleAsync(MetaWithFields("A", "B")).ConfigureAwait(false);

            Assert.That(strategy.LastRead.Count, Is.EqualTo(2));
            Assert.That(strategy.LastRead[0].NodeId, Is.EqualTo(new NodeId(11u)));
            Assert.That(strategy.LastRead[0].AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(strategy.LastRead[1].NodeId, Is.EqualTo(new NodeId(22u)));
        }

        [Test]
        public async Task SampleMapsDataValuesToDataSetFields()
        {
            var sourceTimestamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(new NodeId(11u)));
            DataValue[] readValues =
            [
                new DataValue(
                    new Variant(99), StatusCodes.Good, DateTimeUtc.From(sourceTimestamp))
            ];
            var strategy = new RecordingReadStrategy(readValues.ToArrayOf());
            var source = new ServerPublishedDataSetSource(
                config, strategy, MetaDataBuilder(), AdapterTestHelpers.Telemetry());

            PublishedDataSetSnapshot snapshot = await source.SampleAsync(MetaWithFields("Value1")).ConfigureAwait(false);

            DataSetField[] fields = (DataSetField[]?)snapshot.Fields ?? [];
            Assert.That(fields, Has.Length.EqualTo(1));
            Assert.That(fields[0].Name, Is.EqualTo("Value1"));
            Assert.That(fields[0].Value, Is.EqualTo(new Variant(99)));
            Assert.That(fields[0].StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
        }

        [Test]
        public async Task SampleFillsGapsWithBadNoData()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS",
                AdapterTestHelpers.Variable.Value(new NodeId(11u)),
                AdapterTestHelpers.Variable.Value(new NodeId(22u)));
            var strategy = new RecordingReadStrategy(new[]
            {
                new DataValue(new Variant(1))
            }.ToArrayOf());
            var source = new ServerPublishedDataSetSource(
                config, strategy, MetaDataBuilder(), AdapterTestHelpers.Telemetry());

            PublishedDataSetSnapshot snapshot = await source.SampleAsync(MetaWithFields("A", "B")).ConfigureAwait(false);

            DataSetField[] fields = (DataSetField[]?)snapshot.Fields ?? [];
            Assert.That(fields, Has.Length.EqualTo(2));
            Assert.That(fields[1].StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoData));
        }

        [Test]
        public async Task SampleDelegatesMetaDataResolutionToBuilderEachCycle()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(new NodeId(11u)));
            var builder = new Mock<IDataSetMetaDataBuilder>();
            builder.Setup(b => b.ResolveAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DataSetMetaDataType>(new DataSetMetaDataType()));
            var strategy = new RecordingReadStrategy(new[]
            {
                new DataValue(new Variant(1))
            }.ToArrayOf());
            var source = new ServerPublishedDataSetSource(
                config, strategy, builder.Object, AdapterTestHelpers.Telemetry());

            await source.SampleAsync(MetaWithFields("A")).ConfigureAwait(false);
            await source.SampleAsync(MetaWithFields("A")).ConfigureAwait(false);

            // The source delegates resolution to the builder every cycle; the
            // builder owns caching/retry (see DataSetMetaDataBuilderTests) so a
            // recovered server read re-resolves and re-emits metadata.
            builder.Verify(
                b => b.ResolveAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Test]
        public void SampleCanceledThrows()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(new NodeId(11u)));
            var source = new ServerPublishedDataSetSource(
                config,
                new RecordingReadStrategy(ArrayOf<DataValue>.Empty),
                MetaDataBuilder(),
                AdapterTestHelpers.Telemetry());
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await source.SampleAsync(MetaWithFields("A"), cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }


        [Test]
        public void SourceForwardsMetaDataChangedFromBuilder()
        {
            var builder = new Mock<IDataSetMetaDataBuilder>();
            builder.Setup(b => b.BuildMetaData()).Returns(new DataSetMetaDataType { Name = "Meta" });
            builder.Setup(b => b.ResolveAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DataSetMetaDataType>(new DataSetMetaDataType()));
            var source = new ServerPublishedDataSetSource(
                new PublishedDataSetDataType(),
                new RecordingReadStrategy(ArrayOf<DataValue>.Empty),
                builder.Object,
                AdapterTestHelpers.Telemetry());
            int changeCount = 0;
            ((IMetaDataChangeNotifier)source).MetaDataChanged += (_, _) => changeCount++;

            builder.Raise(b => b.MetaDataChanged += null, EventArgs.Empty);

            Assert.That(changeCount, Is.EqualTo(1));
        }
    }
}
