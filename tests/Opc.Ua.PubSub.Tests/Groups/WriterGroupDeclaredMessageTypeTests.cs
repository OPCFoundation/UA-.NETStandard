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
    /// Validates that <see cref="WriterGroup"/> honours the message type a
    /// <see cref="PublishedDataSetSnapshot"/> declares, instead of deriving
    /// the message type from its own key-frame cadence.
    /// </summary>
    /// <remarks>
    /// A source that returns its complete current state every cycle leaves the
    /// declaration unset and the writer derives key and delta frames itself,
    /// as described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.5">
    /// Part 14 §6.2.5</see>. A source whose samples are occurrences cannot use
    /// that derivation, because consecutive samples carry different fields.
    /// </remarks>
    [TestFixture]
    [TestSpec("6.2.5", Summary = "WriterGroup honours a declared message type")]
    public class WriterGroupDeclaredMessageTypeTests
    {
        [Test]
        [TestSpec("6.2.5")]
        public async Task PublishOnceAsync_HonoursTheDeclaredMessageTypeAsync()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var source = new DeclaringSource
            {
                Declared = PubSubDataSetMessageType.Event
            };
            var captured = new List<PubSubNetworkMessage>();
            WriterGroup group = BuildGroup(clock, captured, source);

            source.Value = 1.0;
            await group.PublishOnceAsync().ConfigureAwait(false);

            Assert.That(captured, Has.Count.EqualTo(1));
            var message = (UadpDataSetMessageV2)
                ((UadpNetworkMessageV2)captured[0]).DataSetMessages[0];
            Assert.That(message.MessageType,
                Is.EqualTo(PubSubDataSetMessageType.Event));
        }

        [Test]
        [TestSpec("6.2.5")]
        public async Task PublishOnceAsync_DeclaredSamplesNeverBecomeDeltaFramesAsync()
        {
            //
            // Successive occurrences are unrelated, so the positional delta
            // derivation must not be applied to them. Every occurrence is
            // published whole and none is suppressed as unchanged.
            //
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var source = new DeclaringSource
            {
                Declared = PubSubDataSetMessageType.Event
            };
            var captured = new List<PubSubNetworkMessage>();
            WriterGroup group = BuildGroup(clock, captured, source);

            source.Value = 1.0;
            await group.PublishOnceAsync().ConfigureAwait(false);
            await group.PublishOnceAsync().ConfigureAwait(false);

            Assert.That(captured, Has.Count.EqualTo(2));
            foreach (PubSubNetworkMessage network in captured)
            {
                var message = (UadpDataSetMessageV2)
                    ((UadpNetworkMessageV2)network).DataSetMessages[0];
                Assert.Multiple(() =>
                {
                    Assert.That(message.MessageType,
                        Is.EqualTo(PubSubDataSetMessageType.Event));
                    Assert.That(message.Fields.Count, Is.EqualTo(1));
                });
            }
        }

        [Test]
        [TestSpec("6.2.5")]
        public async Task PublishOnceAsync_UndeclaredSamplesKeepTheKeyFrameCadenceAsync()
        {
            //
            // A source that declares nothing must behave exactly as before.
            //
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var source = new DeclaringSource { Declared = null };
            var captured = new List<PubSubNetworkMessage>();
            WriterGroup group = BuildGroup(clock, captured, source);

            source.Value = 1.0;
            await group.PublishOnceAsync().ConfigureAwait(false);
            source.Value = 2.0;
            await group.PublishOnceAsync().ConfigureAwait(false);

            Assert.That(captured, Has.Count.EqualTo(2));
            var first = (UadpDataSetMessageV2)
                ((UadpNetworkMessageV2)captured[0]).DataSetMessages[0];
            var second = (UadpDataSetMessageV2)
                ((UadpNetworkMessageV2)captured[1]).DataSetMessages[0];
            Assert.Multiple(() =>
            {
                Assert.That(first.MessageType,
                    Is.EqualTo(PubSubDataSetMessageType.KeyFrame));
                Assert.That(second.MessageType,
                    Is.EqualTo(PubSubDataSetMessageType.DeltaFrame));
            });
        }

        private static WriterGroup BuildGroup(
            TimeProvider clock,
            List<PubSubNetworkMessage> sink,
            DeclaringSource source)
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
                    PublishedData = [new PublishedVariableDataType()]
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

        private sealed class DeclaringSource : IPublishedDataSetSource
        {
            public double Value { get; set; }
            public PubSubDataSetMessageType? Declared { get; set; }

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
                        DateTimeUtc.From(DateTimeOffset.UtcNow),
                        Declared));
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
