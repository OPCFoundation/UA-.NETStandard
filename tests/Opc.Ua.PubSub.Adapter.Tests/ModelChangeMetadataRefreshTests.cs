// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
    /// Unit tests for model-change-triggered metadata refresh wiring.
    /// </summary>
    [TestFixture]
    public sealed class ModelChangeMetadataRefreshTests
    {
        private sealed class EmptyReadStrategy : IReadStrategy
        {
            public ValueTask<ArrayOf<DataValue>> ReadAsync(
                ArrayOf<ReadValueId> nodesToRead,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ArrayOf<DataValue>>([]);
            }
        }

        [Test]
        public async Task ResolveStartsModelChangeMonitoringAtMostOnce()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS",
                AdapterTestHelpers.Variable.Value(new NodeId(42u)));
            config.DataSetMetaData = new DataSetMetaDataType
            {
                Fields = new[]
                {
                    new FieldMetaData
                    {
                        Name = "Value",
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar
                    }
                }.ToArrayOf()
            };
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session.Setup(s => s.StartModelChangeMonitoringAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            using var builder = new DataSetMetaDataBuilder(
                config,
                session.Object,
                AdapterTestHelpers.Telemetry());

            await builder.ResolveAsync().ConfigureAwait(false);
            await builder.ResolveAsync().ConfigureAwait(false);

            session.Verify(
                s => s.StartModelChangeMonitoringAsync(It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ModelChangedRefreshesMetadataAndSourceNotifies()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS",
                AdapterTestHelpers.Variable.Value(new NodeId(42u)));
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session.Setup(s => s.StartModelChangeMonitoringAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            int readCount = 0;
            session.Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    int ordinal = Interlocked.Increment(ref readCount);
                    return new ValueTask<ArrayOf<DataValue>>(ordinal == 1
                        ? CreateTypeResults(DataTypeIds.Int32)
                        : CreateTypeResults(DataTypeIds.Double));
                });
            using var builder = new DataSetMetaDataBuilder(
                config,
                session.Object,
                AdapterTestHelpers.Telemetry());
            var source = new ServerPublishedDataSetSource(
                config,
                new EmptyReadStrategy(),
                builder,
                AdapterTestHelpers.Telemetry());

            await builder.ResolveAsync().ConfigureAwait(false);

            var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.MetaDataChanged += (_, _) => changed.TrySetResult(true);

            session.Raise(s => s.ModelChanged += null, EventArgs.Empty);

            await changed.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            DataSetMetaDataType metaData = builder.BuildMetaData();
            Assert.That(readCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(metaData.Fields[0].DataType, Is.EqualTo(DataTypeIds.Double));
        }

        [Test]
        public async Task ModelChangedWhileRefreshRunsTriggersTrailingRefresh()
        {
            PublishedDataSetDataType config = AdapterTestHelpers.PublishedDataSet(
                "PDS",
                AdapterTestHelpers.Variable.Value(new NodeId(42u)));
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session.Setup(s => s.StartModelChangeMonitoringAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var firstRefreshEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var trailingRefreshCompleted =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int readCount = 0;
            int refreshCount = 0;
            session.Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<ArrayOf<DataValue>>(ReadTypeAsync()));
            using var builder = new DataSetMetaDataBuilder(
                config,
                session.Object,
                AdapterTestHelpers.Telemetry());

            await builder.ResolveAsync().ConfigureAwait(false);
            builder.MetaDataChanged += (_, _) =>
            {
                if (Interlocked.Increment(ref refreshCount) == 2)
                {
                    trailingRefreshCompleted.TrySetResult(true);
                }
            };
            session.Raise(s => s.ModelChanged += null, EventArgs.Empty);
            await firstRefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            session.Raise(s => s.ModelChanged += null, EventArgs.Empty);
            releaseFirstRefresh.SetResult(true);

            await trailingRefreshCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref readCount), Is.EqualTo(3));

            DataSetMetaDataType metaData = builder.BuildMetaData();
            Assert.That(metaData.Fields[0].DataType, Is.EqualTo(DataTypeIds.Double));

            async Task<ArrayOf<DataValue>> ReadTypeAsync()
            {
                int ordinal = Interlocked.Increment(ref readCount);
                if (ordinal == 2)
                {
                    firstRefreshEntered.SetResult(true);
                    await releaseFirstRefresh.Task.ConfigureAwait(false);
                    return CreateTypeResults(DataTypeIds.Int32);
                }
                return CreateTypeResults(ordinal >= 3 ? DataTypeIds.Double : DataTypeIds.Int16);
            }
        }

        private static ArrayOf<DataValue> CreateTypeResults(NodeId dataType)
        {
            return new[]
            {
                new DataValue(new Variant(dataType)),
                new DataValue(new Variant(ValueRanks.Scalar)),
                new DataValue(Variant.Null)
            }.ToArrayOf();
        }
    }
}
