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
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.Tests;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Groups
{
    /// <summary>
    /// Covers the constructor guard-clauses, the
    /// <see cref="DataSetReader.Matches"/> WriterGroupId and PublisherId
    /// filters, <see cref="DataSetReader.DispatchAsync"/> dispatch paths,
    /// and <see cref="DataSetReader.IsReceiveTimedOut"/> timeout logic.
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.9", Summary = "DataSetReader construction, filtering and dispatch")]
    public class DataSetReaderTests
    {
        // ── Constructor ──────────────────────────────────────────────────────

        [Test]
        public void Constructor_NullConfiguration_ThrowsArgumentNullException()
        {
            Assert.That(
                () => new DataSetReader(
                    null!,
                    NullSink.Instance,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
        }

        [Test]
        public void Constructor_NullSink_ThrowsArgumentNullException()
        {
            var cfg = new DataSetReaderDataType { Name = "r", DataSetWriterId = 1 };
            Assert.That(
                () => new DataSetReader(
                    cfg,
                    null!,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("sink"));
        }

        [Test]
        public void Constructor_NullTimeProvider_ThrowsArgumentNullException()
        {
            var cfg = new DataSetReaderDataType { Name = "r", DataSetWriterId = 1 };
            Assert.That(
                () => new DataSetReader(
                    cfg,
                    NullSink.Instance,
                    NUnitTelemetryContext.Create(),
                    null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("timeProvider"));
        }

        [Test]
        public void Constructor_ValidArguments_SetsExpectedProperties()
        {
            var cfg = new DataSetReaderDataType
            {
                Name = "my-reader",
                DataSetWriterId = 7,
                WriterGroupId = 3,
                MessageReceiveTimeout = 5000
            };
            var reader = new DataSetReader(
                cfg,
                NullSink.Instance,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.Multiple(() =>
            {
                Assert.That(reader.Name, Is.EqualTo("my-reader"));
                Assert.That(reader.DataSetWriterId, Is.EqualTo((ushort)7));
                Assert.That(reader.WriterGroupId, Is.EqualTo((ushort)3));
                Assert.That(reader.MessageReceiveTimeout,
                    Is.EqualTo(TimeSpan.FromMilliseconds(5000)));
            });
        }

        // ── Matches – WriterGroupId filter ───────────────────────────────────

        [Test]
        [TestSpec("6.2.9")]
        public void Matches_NullNetworkMessage_ReturnsFalse()
        {
            DataSetReader reader = BuildReader(writerGroupId: 0);
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 5 };

            Assert.That(reader.Matches(null!, dsm), Is.False);
        }

        [Test]
        [TestSpec("6.2.9")]
        public void Matches_NullDataSetMessage_ReturnsFalse()
        {
            DataSetReader reader = BuildReader(writerGroupId: 0);
            var net = new UadpNetworkMessageV2();

            Assert.That(reader.Matches(net, null!), Is.False);
        }

        [Test]
        [TestSpec("6.2.9")]
        public void Matches_WriterGroupIdZero_AcceptsAnyGroup()
        {
            // WriterGroupId == 0 on the reader means "accept any group"
            DataSetReader reader = BuildReader(writerId: 0, writerGroupId: 0);
            var net = new UadpNetworkMessageV2 { WriterGroupId = 99 };
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 1 };

            Assert.That(reader.Matches(net, dsm), Is.True);
        }

        [Test]
        [TestSpec("6.2.9")]
        public void Matches_WriterGroupIdMatch_Accepts()
        {
            DataSetReader reader = BuildReader(writerId: 0, writerGroupId: 7);
            var net = new UadpNetworkMessageV2 { WriterGroupId = 7 };
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 1 };

            Assert.That(reader.Matches(net, dsm), Is.True);
        }

        [Test]
        [TestSpec("6.2.9")]
        public void Matches_WriterGroupIdMismatch_Rejects()
        {
            DataSetReader reader = BuildReader(writerId: 0, writerGroupId: 7);
            var net = new UadpNetworkMessageV2 { WriterGroupId = 99 };
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 1 };

            Assert.That(reader.Matches(net, dsm), Is.False);
        }

        [Test]
        [TestSpec("6.2.9")]
        public void Matches_NetworkMessageWriterGroupIdAbsent_Accepts()
        {
            // null WriterGroupId on the message means the group header was omitted;
            // per spec the filter must not apply in that case.
            DataSetReader reader = BuildReader(writerId: 0, writerGroupId: 7);
            var net = new UadpNetworkMessageV2 { WriterGroupId = null };
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 1 };

            Assert.That(reader.Matches(net, dsm), Is.True);
        }

        // ── Matches – PublisherId filter ─────────────────────────────────────

        [Test]
        [TestSpec("6.2.9")]
        public void Matches_NullPublisherId_AcceptsAnyPublisher()
        {
            // default Variant → IsNull → no publisher filter applied
            DataSetReader reader = BuildReader(publisherId: default);
            var net = new UadpNetworkMessageV2
            {
                PublisherId = PublisherId.FromUInt16(42)
            };
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 5 };

            Assert.That(reader.Matches(net, dsm), Is.True);
        }

        [Test]
        [TestSpec("6.2.9")]
        public void Matches_ExpectedPublisherIdMatch_Accepts()
        {
            DataSetReader reader = BuildReader(publisherId: new Variant((ushort)42));
            var net = new UadpNetworkMessageV2
            {
                PublisherId = PublisherId.FromUInt16(42)
            };
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 5 };

            Assert.That(reader.Matches(net, dsm), Is.True);
        }

        [Test]
        [TestSpec("6.2.9")]
        public void Matches_ExpectedPublisherIdMismatch_Rejects()
        {
            DataSetReader reader = BuildReader(publisherId: new Variant((ushort)42));
            var net = new UadpNetworkMessageV2
            {
                PublisherId = PublisherId.FromUInt16(99)
            };
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 5 };

            Assert.That(reader.Matches(net, dsm), Is.False);
        }

        // ── DispatchAsync ────────────────────────────────────────────────────

        [Test]
        public void DispatchAsync_NullDataSetMessage_ThrowsArgumentNullException()
        {
            DataSetReader reader = BuildReader();
            Assert.That(
                async () => await reader.DispatchAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("dataSetMessage"));
        }

        [Test]
        public async Task DispatchAsync_DisabledState_DoesNotCallSinkAsync()
        {
            var countingSink = new CountingSink();
            DataSetReader reader = BuildReader(sink: countingSink);
            // Do NOT enable — initial state is Disabled
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 5 };

            await reader.DispatchAsync(dsm).ConfigureAwait(false);

            Assert.That(countingSink.CallCount, Is.Zero,
                "Disabled reader must not forward to its sink.");
        }

        [Test]
        public async Task DispatchAsync_OperationalState_CallsSinkWithFieldsAsync()
        {
            var countingSink = new CountingSink();
            DataSetReader reader = BuildReader(sink: countingSink);
            _ = reader.State.TryEnable();
            _ = reader.State.TryMarkOperational();

            var fields = new DataSetField[]
            {
                new() { Name = "f1", Value = new Variant(1) }
            };
            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 5, Fields = fields };

            await reader.DispatchAsync(dsm).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(countingSink.CallCount, Is.EqualTo(1));
                Assert.That(countingSink.LastFields, Is.SameAs(fields));
            });
        }

        [Test]
        public async Task DispatchAsync_SinkThrowsNonOce_FaultsStateAndSwallowsAsync()
        {
            var throwingSink = new ThrowingSink(new InvalidOperationException("boom"));
            DataSetReader reader = BuildReader(sink: throwingSink);
            _ = reader.State.TryEnable();
            _ = reader.State.TryMarkOperational();

            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 5 };

            // Non-OCE must be caught and the reader faulted — never rethrown.
            await reader.DispatchAsync(dsm).ConfigureAwait(false);

            Assert.That(reader.State.State, Is.EqualTo(PubSubState.Error),
                "Non-OCE from the sink must transition the reader to Error.");
        }

        [Test]
        public void DispatchAsync_SinkThrowsOce_Propagates()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var throwingSink = new ThrowingSink(new OperationCanceledException(cts.Token));
            DataSetReader reader = BuildReader(sink: throwingSink);
            _ = reader.State.TryEnable();
            _ = reader.State.TryMarkOperational();

            var dsm = new UadpDataSetMessageV2 { DataSetWriterId = 5 };

            Assert.That(
                async () => await reader.DispatchAsync(dsm, cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        // ── IsReceiveTimedOut ────────────────────────────────────────────────

        [Test]
        public void IsReceiveTimedOut_ZeroTimeout_AlwaysFalse()
        {
            // MessageReceiveTimeout == 0 means "no timeout configured"
            DataSetReader reader = BuildReader(timeoutMs: 0);

            Assert.That(reader.IsReceiveTimedOut(), Is.False);
        }

        [Test]
        public void IsReceiveTimedOut_BeforeTimeout_ReturnsFalse()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            DataSetReader reader = BuildReader(timeoutMs: 5000, clock: clock);

            clock.Advance(TimeSpan.FromMilliseconds(100));

            Assert.That(reader.IsReceiveTimedOut(), Is.False);
        }

        [Test]
        public void IsReceiveTimedOut_AfterTimeout_ReturnsTrue()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            DataSetReader reader = BuildReader(timeoutMs: 500, clock: clock);

            clock.Advance(TimeSpan.FromMilliseconds(750));

            Assert.That(reader.IsReceiveTimedOut(), Is.True);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static DataSetReader BuildReader(
            ushort writerId = 5,
            ushort writerGroupId = 0,
            Variant publisherId = default,
            int timeoutMs = 0,
            ISubscribedDataSetSink? sink = null,
            TimeProvider? clock = null)
        {
            var cfg = new DataSetReaderDataType
            {
                Name = "test-reader",
                DataSetWriterId = writerId,
                WriterGroupId = writerGroupId,
                MessageReceiveTimeout = timeoutMs,
                PublisherId = publisherId
            };
            return new DataSetReader(
                cfg,
                sink ?? NullSink.Instance,
                NUnitTelemetryContext.Create(),
                clock ?? TimeProvider.System);
        }

        private sealed class NullSink : ISubscribedDataSetSink
        {
            public static NullSink Instance { get; } = new();

            public ValueTask WriteAsync(
                IReadOnlyList<DataSetField> fields,
                CancellationToken cancellationToken = default)
                => default;
        }

        private sealed class CountingSink : ISubscribedDataSetSink
        {
            public int CallCount { get; private set; }
            public IReadOnlyList<DataSetField>? LastFields { get; private set; }

            public ValueTask WriteAsync(
                IReadOnlyList<DataSetField> fields,
                CancellationToken cancellationToken = default)
            {
                CallCount++;
                LastFields = fields;
                return default;
            }
        }

        private sealed class ThrowingSink : ISubscribedDataSetSink
        {
            private readonly Exception m_exception;

            public ThrowingSink(Exception exception)
            {
                m_exception = exception;
            }

            public ValueTask WriteAsync(
                IReadOnlyList<DataSetField> fields,
                CancellationToken cancellationToken = default)
                => throw m_exception;
        }
    }
}
