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
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DataSetMetaDataBuilder"/>: config-first
    /// metadata, server-fallback attribute reads and fail-soft defaults.
    /// </summary>
    [TestFixture]
    public sealed class DataSetMetaDataBuilderTests
    {
        [Test]
        public void ConstructorNullConfigurationThrows()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();

            Assert.That(
                () => new DataSetMetaDataBuilder(
                    null!, session.Object, AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
        }

        [Test]
        public void ConstructorNullSessionThrows()
        {
            Assert.That(
                () => new DataSetMetaDataBuilder(
                    new PublishedDataSetDataType(), null!, AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("session"));
        }

        [Test]
        public void ConstructorNullTelemetryThrows()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();

            Assert.That(
                () => new DataSetMetaDataBuilder(
                    new PublishedDataSetDataType(), session.Object, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public void BuildMetaDataUsesDefaultFieldNamesFromConfiguration()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS",
                AdapterTestHelpers.Variable.Value(new NodeId(1u)),
                AdapterTestHelpers.Variable.Value(new NodeId(2u)));
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            using var builder = new DataSetMetaDataBuilder(
                config, session.Object, AdapterTestHelpers.Telemetry());

            DataSetMetaDataType metaData = builder.BuildMetaData();

            Assert.That(metaData.Fields.Count, Is.EqualTo(2));
            Assert.That(metaData.Fields[0].Name, Is.EqualTo("Field1"));
            Assert.That(metaData.Fields[1].Name, Is.EqualTo("Field2"));
        }

        [Test]
        public async Task ResolveReadsServerTypeWhenConfigLacksIt()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(new NodeId(42u)));
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<DataValue>>(new[]
                {
                    new DataValue(new Variant(DataTypeIds.Int32)),
                    new DataValue(new Variant(ValueRanks.Scalar)),
                    new DataValue(Variant.Null)
                }.ToArrayOf()));

            using var builder = new DataSetMetaDataBuilder(
                config, session.Object, AdapterTestHelpers.Telemetry());

            DataSetMetaDataType metaData = await builder.ResolveAsync().ConfigureAwait(false);

            Assert.That(metaData.Fields[0].BuiltInType, Is.EqualTo((byte)BuiltInType.Int32));
            Assert.That(metaData.Fields[0].DataType, Is.EqualTo(DataTypeIds.Int32));
            session.Verify(
                s => s.ReadAsync(It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ResolveUsesConfiguredTypeWithoutServerRead()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(new NodeId(42u)));
            config.DataSetMetaData = new DataSetMetaDataType
            {
                Fields = new[]
                {
                    new FieldMetaData
                    {
                        Name = "Temperature",
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar
                    }
                }.ToArrayOf()
            };
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();

            using var builder = new DataSetMetaDataBuilder(
                config, session.Object, AdapterTestHelpers.Telemetry());

            DataSetMetaDataType metaData = await builder.ResolveAsync().ConfigureAwait(false);

            Assert.That(metaData.Fields[0].Name, Is.EqualTo("Temperature"));
            Assert.That(metaData.Fields[0].BuiltInType, Is.EqualTo((byte)BuiltInType.Double));
            session.Verify(
                s => s.ReadAsync(It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ResolveUsesDefaultsWhenServerReadFaults()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(new NodeId(42u)));
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadServerHalted));

            using var builder = new DataSetMetaDataBuilder(
                config, session.Object, AdapterTestHelpers.Telemetry());

            DataSetMetaDataType metaData = await builder.ResolveAsync().ConfigureAwait(false);

            Assert.That(metaData.Fields[0].BuiltInType, Is.EqualTo((byte)BuiltInType.Variant));
            Assert.That(metaData.Fields[0].DataType, Is.EqualTo(DataTypeIds.BaseDataType));
        }

        [Test]
        public async Task ResolveIsIdempotent()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(new NodeId(42u)));
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<DataValue>>(new[]
                {
                    new DataValue(new Variant(DataTypeIds.Int32)),
                    new DataValue(new Variant(ValueRanks.Scalar)),
                    new DataValue(Variant.Null)
                }.ToArrayOf()));

            using var builder = new DataSetMetaDataBuilder(
                config, session.Object, AdapterTestHelpers.Telemetry());

            await builder.ResolveAsync().ConfigureAwait(false);
            await builder.ResolveAsync().ConfigureAwait(false);

            session.Verify(
                s => s.ReadAsync(It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }


        [Test]
        public async Task ResolveRetriesAfterFailureAndUsesRecoveredServerType()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(new NodeId(42u)));
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            ArrayOf<DataValue> goodResults = CreateDoubleTypeResults();
            session.SetupSequence(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Throws(ServiceResultException.Create(StatusCodes.BadConnectionClosed, "x"))
                .Returns(new ValueTask<ArrayOf<DataValue>>(goodResults));
            using var builder = new DataSetMetaDataBuilder(
                config, session.Object, AdapterTestHelpers.Telemetry());

            await builder.ResolveAsync().ConfigureAwait(false);
            DataSetMetaDataType failedMetaData = builder.BuildMetaData();
            await builder.ResolveAsync().ConfigureAwait(false);
            DataSetMetaDataType recoveredMetaData = builder.BuildMetaData();

            Assert.That(failedMetaData.Fields[0].BuiltInType, Is.EqualTo((byte)BuiltInType.Variant));
            Assert.That(failedMetaData.Fields[0].DataType, Is.EqualTo(DataTypeIds.BaseDataType));
            Assert.That(
                recoveredMetaData.Fields[0].BuiltInType,
                Is.EqualTo((byte)TypeInfo.GetBuiltInType(DataTypeIds.Double)));
            Assert.That(recoveredMetaData.Fields[0].DataType, Is.EqualTo(DataTypeIds.Double));
            session.Verify(
                s => s.ReadAsync(It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Test]
        public async Task RefreshRaisesMetaDataChangedWhenRecoveredMetadataChanges()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS", AdapterTestHelpers.Variable.Value(new NodeId(42u)));
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            ArrayOf<DataValue> goodResults = CreateDoubleTypeResults();
            session.SetupSequence(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Throws(ServiceResultException.Create(StatusCodes.BadConnectionClosed, "x"))
                .Returns(new ValueTask<ArrayOf<DataValue>>(goodResults))
                .Returns(new ValueTask<ArrayOf<DataValue>>(goodResults));
            using var builder = new DataSetMetaDataBuilder(
                config, session.Object, AdapterTestHelpers.Telemetry());

            await builder.ResolveAsync().ConfigureAwait(false);
            int changeCount = 0;
            builder.MetaDataChanged += (_, _) => changeCount++;
            bool changed = await builder.RefreshAsync().ConfigureAwait(false);
            bool unchanged = await builder.RefreshAsync().ConfigureAwait(false);

            Assert.That(changed, Is.True);
            Assert.That(unchanged, Is.False);
            Assert.That(changeCount, Is.GreaterThanOrEqualTo(1));
        }

        private static ArrayOf<DataValue> CreateDoubleTypeResults()
        {
            return new[]
            {
                new DataValue(new Variant(DataTypeIds.Double)),
                new DataValue(new Variant(ValueRanks.Scalar)),
                new DataValue(Variant.Null)
            }.ToArrayOf();
        }
    }
}
