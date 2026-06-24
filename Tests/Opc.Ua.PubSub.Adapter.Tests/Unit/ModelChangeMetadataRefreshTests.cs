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
using Opc.Ua.PubSub.DataSets;

namespace Opc.Ua.PubSub.Adapter.Tests.Unit
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
                return new ValueTask<ArrayOf<DataValue>>(ArrayOf<DataValue>.Empty);
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

            await builder.ResolveAsync();
            await builder.ResolveAsync();

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

            await builder.ResolveAsync();

            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ((IMetaDataChangeNotifier)source).MetaDataChanged += (_, _) => changed.TrySetResult();

            session.Raise(s => s.ModelChanged += null, EventArgs.Empty);

            await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));

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
            var firstRefreshEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int readCount = 0;
            session.Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<ArrayOf<DataValue>>(ReadTypeAsync()));
            using var builder = new DataSetMetaDataBuilder(
                config,
                session.Object,
                AdapterTestHelpers.Telemetry());

            await builder.ResolveAsync();
            session.Raise(s => s.ModelChanged += null, EventArgs.Empty);
            await firstRefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            session.Raise(s => s.ModelChanged += null, EventArgs.Empty);
            releaseFirstRefresh.SetResult();

            await WaitForReadCountAsync(() => Volatile.Read(ref readCount), 3).ConfigureAwait(false);

            DataSetMetaDataType metaData = builder.BuildMetaData();
            Assert.That(metaData.Fields[0].DataType, Is.EqualTo(DataTypeIds.Double));

            async Task<ArrayOf<DataValue>> ReadTypeAsync()
            {
                int ordinal = Interlocked.Increment(ref readCount);
                if (ordinal == 2)
                {
                    firstRefreshEntered.SetResult();
                    await releaseFirstRefresh.Task.ConfigureAwait(false);
                    return CreateTypeResults(DataTypeIds.Int32);
                }
                return CreateTypeResults(ordinal >= 3 ? DataTypeIds.Double : DataTypeIds.Int16);
            }
        }

        private static async Task WaitForReadCountAsync(Func<int> readCount, int expected)
        {
            for (int i = 0; i < 20; i++)
            {
                if (readCount() >= expected)
                {
                    return;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            Assert.Fail("Timed out waiting for trailing metadata refresh.");
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
