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
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.Tests;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Groups
{
    /// <summary>
    /// Validates that <see cref="WriterGroup"/> emits a properly typed
    /// <c>KeepAlive</c> <see cref="PubSubDataSetMessage"/> when the
    /// configured <c>KeepAliveTime</c> elapses without any data sample.
    /// </summary>
    /// <remarks>
    /// Covers
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.9.6">
    /// Part 14 §6.2.9.6 KeepAlive</see>,
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.5">
    /// §7.2.4.5.5 KeepAlive (UADP)</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.2">
    /// §7.2.5.2 KeepAlive (JSON)</see>.
    /// </remarks>
    [TestFixture]
    [TestSpec("6.2.9.6", Summary = "WriterGroup emits a KeepAlive DataSetMessage")]
    [TestSpec("7.2.4.5.5")]
    public class WriterGroupKeepAliveTests
    {
        [Test]
        public async Task PublishOnceAsync_EmitsKeepAliveAfterKeepAliveTimeElapsesAsync()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var captured = new List<PubSubNetworkMessage>();
            WriterGroup group = BuildGroup(clock, captured);

            // First publish emits a KeyFrame with empty fields.
            await group.PublishOnceAsync().ConfigureAwait(false);
            Assert.That(captured, Has.Count.EqualTo(1));
            Assert.That(captured[0], Is.InstanceOf<UadpNetworkMessageV2>());
            UadpNetworkMessageV2 first = (UadpNetworkMessageV2)captured[0];
            Assert.That(((PubSubDataSetMessage[]?)first.DataSetMessages) ?? [], Has.Length.EqualTo(1));
            Assert.That(((UadpDataSetMessageV2)first.DataSetMessages[0]).MessageType,
                Is.EqualTo(PubSubDataSetMessageType.KeyFrame));

            // Second publish without elapsed time → empty delta path returns
            // null, KeepAlive not yet due → no message published.
            captured.Clear();
            await group.PublishOnceAsync().ConfigureAwait(false);
            Assert.That(captured, Is.Empty,
                "No KeepAlive expected before KeepAliveTime elapses.");

            // Advance the clock past the configured KeepAliveTime.
            clock.Advance(TimeSpan.FromMilliseconds(750));
            await group.PublishOnceAsync().ConfigureAwait(false);

            Assert.That(captured, Has.Count.EqualTo(1),
                "KeepAlive must be emitted once KeepAliveTime elapses.");
            UadpNetworkMessageV2 keepAlive = (UadpNetworkMessageV2)captured[0];
            Assert.That(((PubSubDataSetMessage[]?)keepAlive.DataSetMessages) ?? [], Has.Length.EqualTo(1));
            UadpDataSetMessageV2 ds = (UadpDataSetMessageV2)keepAlive.DataSetMessages[0];
            Assert.Multiple(() =>
            {
                Assert.That(ds.MessageType, Is.EqualTo(PubSubDataSetMessageType.KeepAlive));
                Assert.That(((DataSetField[]?)ds.Fields) ?? [], Is.Empty,
                    "KeepAlive DataSetMessage must carry an empty field list.");
                Assert.That(ds.DataSetWriterId, Is.EqualTo((ushort)42));
                Assert.That(ds.SequenceNumber, Is.GreaterThan(0u));
            });
        }

        private static WriterGroup BuildGroup(
            TimeProvider clock,
            List<PubSubNetworkMessage> sink)
        {
            var pdsConfig = new PublishedDataSetDataType
            {
                Name = "pds"
            };
            var pds = new PublishedDataSet(pdsConfig, EmptyDataSetSource.Instance);
            var writerConfig = new DataSetWriterDataType
            {
                Name = "writer",
                DataSetWriterId = 42,
                DataSetName = "pds",
                KeyFrameCount = 10
            };
            var writer = new DataSetWriter(writerConfig, pds, NUnitTelemetryContext.Create());

            var groupConfig = new WriterGroupDataType
            {
                Name = "group",
                WriterGroupId = 7,
                PublishingInterval = 100,
                KeepAliveTime = 500
            };
            var schedule = new PubSubSchedule(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(500),
                TimeSpan.Zero,
                TimeSpan.Zero);
            var group = new WriterGroup(
                groupConfig,
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

        private sealed class EmptyDataSetSource : IPublishedDataSetSource
        {
            public static EmptyDataSetSource Instance { get; } = new();

            public DataSetMetaDataType BuildMetaData()
            {
                return new DataSetMetaDataType();
            }

            public ValueTask<PublishedDataSetSnapshot> SampleAsync(
                DataSetMetaDataType metaData,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<PublishedDataSetSnapshot>(
                    new PublishedDataSetSnapshot(
                        new ConfigurationVersionDataType(),
                        [],
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
