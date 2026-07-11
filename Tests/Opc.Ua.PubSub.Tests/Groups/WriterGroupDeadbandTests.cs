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
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.Tests;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Groups
{
    /// <summary>
    /// Validates that <see cref="WriterGroup"/> consults per-field
    /// deadband on the published variable (DeadbandType + DeadbandValue
    /// from <see cref="PublishedVariableDataType"/>) before emitting a
    /// delta-frame for a sample change.
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.11.1", Summary = "WriterGroup honours per-field deadband")]
    public class WriterGroupDeadbandTests
    {
        [Test]
        [TestSpec("6.2.11.1")]
        public async Task PublishOnceAsync_AbsoluteDeadbandSuppressesSmallChangeAsync()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var source = new SteppingSource();
            var captured = new List<PubSubNetworkMessage>();

            WriterGroup group = BuildGroup(
                clock, captured, source,
                deadbandType: (uint)DeadbandType.Absolute, deadbandValue: 1.0);

            source.Value = 10.0;
            await group.PublishOnceAsync().ConfigureAwait(false); // KeyFrame
            Assert.That(captured, Has.Count.EqualTo(1));

            // Below threshold change → no delta-frame
            source.Value = 10.5;
            captured.Clear();
            await group.PublishOnceAsync().ConfigureAwait(false);
            Assert.That(captured, Is.Empty,
                "Change of 0.5 with absolute deadband 1.0 must be suppressed.");

            // Above threshold change → delta-frame
            source.Value = 12.0;
            await group.PublishOnceAsync().ConfigureAwait(false);
            Assert.That(captured, Has.Count.EqualTo(1));
            var net = (UadpNetworkMessageV2)captured[0];
            var ds = (UadpDataSetMessageV2)net.DataSetMessages[0];
            Assert.That(ds.MessageType, Is.EqualTo(PubSubDataSetMessageType.DeltaFrame));
        }

        [Test]
        [TestSpec("6.2.11.1")]
        public async Task PublishOnceAsync_NoDeadbandPassesAnyChangeAsync()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var source = new SteppingSource();
            var captured = new List<PubSubNetworkMessage>();

            WriterGroup group = BuildGroup(
                clock, captured, source,
                deadbandType: (uint)DeadbandType.None, deadbandValue: 0);

            source.Value = 5.0;
            await group.PublishOnceAsync().ConfigureAwait(false);
            source.Value = 5.0001;
            captured.Clear();
            await group.PublishOnceAsync().ConfigureAwait(false);
            Assert.That(captured, Has.Count.EqualTo(1),
                "Without deadband any value change triggers a delta-frame.");
        }

        private static WriterGroup BuildGroup(
            TimeProvider clock,
            List<PubSubNetworkMessage> sink,
            SteppingSource source,
            uint deadbandType,
            double deadbandValue)
        {
            var pdsConfig = new PublishedDataSetDataType
            {
                Name = "pds",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Fields = [new FieldMetaData { Name = "f" }]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData =
                    [
                        new PublishedVariableDataType
                        {
                            DeadbandType = deadbandType,
                            DeadbandValue = deadbandValue
                        }
                    ]
                })
            };
            var pds = new PublishedDataSet(pdsConfig, source);

            var writerConfig = new DataSetWriterDataType
            {
                Name = "writer",
                DataSetWriterId = 1,
                DataSetName = "pds",
                KeyFrameCount = 5
            };
            var writer = new DataSetWriter(writerConfig, pds, NUnitTelemetryContext.Create());
            var schedule = new PubSubSchedule(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero);
            var group = new WriterGroup(
                new WriterGroupDataType
                {
                    Name = "group",
                    WriterGroupId = 7,
                    PublishingInterval = 100
                },
                [writer],
                schedule,
                NoOpScheduler.Instance,
                NUnitTelemetryContext.Create(),
                clock)
            {
                PublishSink = (msg, ct) =>
                {
                    sink.Add(msg);
                    return default;
                }
            };
            _ = group.State.TryEnable();
            _ = group.State.TryMarkOperational();
            _ = writer.State.TryEnable();
            _ = writer.State.TryMarkOperational();
            return group;
        }

        private sealed class SteppingSource : IPublishedDataSetSource
        {
            public double Value { get; set; }

            public DataSetMetaDataType BuildMetaData()
            {
                return new DataSetMetaDataType
                {
                    Fields = [new FieldMetaData { Name = "f" }]
                };
            }

            public ValueTask<PublishedDataSetSnapshot> SampleAsync(
                DataSetMetaDataType metaData,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<PublishedDataSetSnapshot>(
                    new PublishedDataSetSnapshot(
                        new ConfigurationVersionDataType(),
                        [new DataSetField { Name = "f", Value = new Variant(Value) }],
                        DateTimeUtc.From(DateTimeOffset.UtcNow)));
            }
        }

        private sealed class NoOpScheduler : IPubSubScheduler
        {
            public static NoOpScheduler Instance { get; } = new();

            public ValueTask<IAsyncDisposable> ScheduleAsync(
                PubSubSchedule schedule,
                Func<CancellationToken, ValueTask> action,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncDisposable>(NoOpHandle.Instance);
            }

            private sealed class NoOpHandle : IAsyncDisposable
            {
                public static NoOpHandle Instance { get; } = new();

                public ValueTask DisposeAsync()
                {
                    return default;
                }
            }
        }
    }
}
