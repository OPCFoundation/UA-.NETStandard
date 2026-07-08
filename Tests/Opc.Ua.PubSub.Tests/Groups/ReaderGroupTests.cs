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
using NUnit.Framework;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.Tests;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Groups
{
    /// <summary>
    /// Covers the constructor guard-clauses, property accessors, message
    /// dispatch routing and the Enable / Disable lifecycle of
    /// <see cref="ReaderGroup"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.8", Summary = "ReaderGroup construction, dispatch and lifecycle")]
    public class ReaderGroupTests
    {
        [Test]
        public void Constructor_ShortForm_NullConfiguration_ThrowsArgumentNullException()
        {
            Assert.That(
                () => new ReaderGroup(null!, [], NUnitTelemetryContext.Create()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
        }

        [Test]
        public void Constructor_ShortForm_DefaultReaders_IsEmpty()
        {
            ReaderGroup group = new(
                new ReaderGroupDataType { Name = "g" },
                default,
                NUnitTelemetryContext.Create());
            Assert.That(((IDataSetReader[]?)group.DataSetReaders) ?? [], Is.Empty);
        }

        [Test]
        public void Constructor_ShortForm_NullTelemetry_ThrowsArgumentNullException()
        {
            Assert.That(
                () => new ReaderGroup(
                    new ReaderGroupDataType { Name = "g" },
                    [],
                    null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public void Constructor_LongForm_NullConfiguration_ThrowsArgumentNullException()
        {
            Assert.That(
                () => new ReaderGroup(
                    null!, [], NUnitTelemetryContext.Create(),
                    scheduler: null, diagnostics: null),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
        }

        [Test]
        public void Constructor_LongForm_DefaultReaders_IsEmpty()
        {
            ReaderGroup group = new(
                new ReaderGroupDataType { Name = "g" },
                default,
                NUnitTelemetryContext.Create(),
                scheduler: null,
                diagnostics: null);
            Assert.That(((IDataSetReader[]?)group.DataSetReaders) ?? [], Is.Empty);
        }

        [Test]
        public void Constructor_LongForm_NullTelemetry_ThrowsArgumentNullException()
        {
            Assert.That(
                () => new ReaderGroup(
                    new ReaderGroupDataType { Name = "g" },
                    [], null!,
                    scheduler: null, diagnostics: null),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public void Constructor_SetsNameAndReaderListFromConfiguration()
        {
            DataSetReader r1 = MakeReader(1);
            DataSetReader r2 = MakeReader(2);
            var group = new ReaderGroup(
                new ReaderGroupDataType { Name = "my-group" },
                [r1, r2],
                NUnitTelemetryContext.Create());

            Assert.Multiple(() =>
            {
                Assert.That(group.Name, Is.EqualTo("my-group"));
                Assert.That(group.DataSetReaders.Count, Is.EqualTo(2));
                Assert.That(group.Configuration.Name, Is.EqualTo("my-group"));
            });
        }

        [Test]
        public void DataSetReaders_ReturnsProvidedReaders()
        {
            DataSetReader r = MakeReader(3);
            var group = MakeGroup([r]);

            Assert.That(((IDataSetReader[]?)group.DataSetReaders) ?? [], Is.EquivalentTo([r]));
        }

        [Test]
        public void DispatchAsync_NullNetworkMessage_ThrowsArgumentNullException()
        {
            ReaderGroup group = MakeGroup();
            Assert.That(
                async () => await group.DispatchAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("networkMessage"));
        }

        [Test]
        public async Task DispatchAsync_GroupDisabled_DoesNotRouteToReadersAsync()
        {
            var sink = new CountingSink();
            DataSetReader reader = MakeReaderWithSink(writerId: 0, sink: sink);
            _ = reader.State.TryEnable();
            _ = reader.State.TryMarkOperational();

            // Group is NOT enabled — default state is Disabled.
            var group = MakeGroup([reader]);

            var net = new UadpNetworkMessageV2
            {
                DataSetMessages = [new UadpDataSetMessageV2 { DataSetWriterId = 0 }]
            };

            await group.DispatchAsync(net).ConfigureAwait(false);

            Assert.That(sink.CallCount, Is.Zero,
                "Disabled ReaderGroup must not forward messages to its readers.");
        }

        [Test]
        public async Task DispatchAsync_MatchingReader_ForwardsToSinkAsync()
        {
            var sink = new CountingSink();
            DataSetReader reader = MakeReaderWithSink(writerId: 5, sink: sink);
            _ = reader.State.TryEnable();
            _ = reader.State.TryMarkOperational();

            ReaderGroup group = MakeGroup([reader]);
            _ = group.State.TryEnable();
            _ = group.State.TryMarkOperational();

            var net = new UadpNetworkMessageV2
            {
                DataSetMessages = [new UadpDataSetMessageV2 { DataSetWriterId = 5 }]
            };

            await group.DispatchAsync(net).ConfigureAwait(false);

            Assert.That(sink.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DispatchAsync_NonMatchingReader_SkipsReaderAsync()
        {
            var sink = new CountingSink();
            // Reader expects WriterId=5, message carries WriterId=99
            DataSetReader reader = MakeReaderWithSink(writerId: 5, sink: sink);
            _ = reader.State.TryEnable();
            _ = reader.State.TryMarkOperational();

            ReaderGroup group = MakeGroup([reader]);
            _ = group.State.TryEnable();
            _ = group.State.TryMarkOperational();

            var net = new UadpNetworkMessageV2
            {
                DataSetMessages = [new UadpDataSetMessageV2 { DataSetWriterId = 99 }]
            };

            await group.DispatchAsync(net).ConfigureAwait(false);

            Assert.That(sink.CallCount, Is.Zero,
                "Non-matching WriterId must prevent dispatch to the reader's sink.");
        }

        [Test]
        public void DispatchAsync_SinkThrowsOce_PropagatesOce()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var throwingSink = new ThrowingSink(new OperationCanceledException(cts.Token));
            DataSetReader reader = MakeReaderWithSink(writerId: 0, sink: throwingSink);
            _ = reader.State.TryEnable();
            _ = reader.State.TryMarkOperational();

            ReaderGroup group = MakeGroup([reader]);
            _ = group.State.TryEnable();
            _ = group.State.TryMarkOperational();

            var net = new UadpNetworkMessageV2
            {
                DataSetMessages = [new UadpDataSetMessageV2 { DataSetWriterId = 0 }]
            };

            Assert.That(
                async () => await group.DispatchAsync(net, cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>(),
                "OCE from reader.DispatchAsync must propagate through the group.");
        }

        [Test]
        public async Task EnableAsync_TransitionsGroupToOperationalAsync()
        {
            var group = MakeGroup();

            await group.EnableAsync().ConfigureAwait(false);

            Assert.That(group.State.State, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        [TestSpec("6.2.1", Summary = "DataSetReader remains PreOperational until first DataSetMessage")]
        public async Task EnableAsync_TransitionsAllReadersToPreOperationalAsync()
        {
            DataSetReader r1 = MakeReader(1);
            DataSetReader r2 = MakeReader(2);
            var group = MakeGroup([r1, r2]);

            await group.EnableAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(r1.State.State, Is.EqualTo(PubSubState.PreOperational));
                Assert.That(r2.State.State, Is.EqualTo(PubSubState.PreOperational));
            });
        }

        [Test]
        public async Task EnableAsync_CalledTwice_IsIdempotentAsync()
        {
            var group = MakeGroup();
            await group.EnableAsync().ConfigureAwait(false);
            await group.EnableAsync().ConfigureAwait(false); // must not throw

            Assert.That(group.State.State, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public async Task EnableAsync_WithSchedulerAndDiagnostics_StartsTimeoutWatcherAsync()
        {
            var scheduler = new TrackingScheduler();
            var diagnostics = new PubSubDiagnostics(
                PubSubDiagnosticsLevel.High, TimeProvider.System);
            DataSetReader reader = MakeReader(1);

            var group = new ReaderGroup(
                new ReaderGroupDataType { Name = "g" },
                [reader],
                NUnitTelemetryContext.Create(),
                scheduler,
                diagnostics);

            await group.EnableAsync().ConfigureAwait(false);

            Assert.That(scheduler.ScheduleCallCount, Is.EqualTo(1),
                "Exactly one ScheduleAsync call must register the timeout-watcher poll.");
        }

        [Test]
        public async Task DisableAsync_TransitionsToDisabledAsync()
        {
            var group = MakeGroup();
            await group.EnableAsync().ConfigureAwait(false);
            await group.DisableAsync().ConfigureAwait(false);

            Assert.That(group.State.State, Is.EqualTo(PubSubState.Disabled));
        }

        [Test]
        public async Task DisposeAsync_DisablesGroupAsync()
        {
            var group = MakeGroup();
            await group.EnableAsync().ConfigureAwait(false);
            await group.DisposeAsync().ConfigureAwait(false);

            Assert.That(group.State.State, Is.EqualTo(PubSubState.Disabled));
        }

        [Test]
        public async Task DisableAsync_ThenEnableAsync_WithScheduler_RestartsTimeoutWatcherAsync()
        {
            var scheduler = new TrackingScheduler();
            var diagnostics = new PubSubDiagnostics(
                PubSubDiagnosticsLevel.High, TimeProvider.System);
            DataSetReader reader = MakeReader(1);

            var group = new ReaderGroup(
                new ReaderGroupDataType { Name = "g" },
                [reader],
                NUnitTelemetryContext.Create(),
                scheduler,
                diagnostics);

            await group.EnableAsync().ConfigureAwait(false);   // watcher started (count = 1)
            await group.DisableAsync().ConfigureAwait(false);  // watcher stopped
            await group.EnableAsync().ConfigureAwait(false);   // watcher started again (count = 2)

            Assert.That(scheduler.ScheduleCallCount, Is.EqualTo(2),
                "A second Enable after Disable must restart the timeout-watcher schedule.");
        }

        private static DataSetReader MakeReader(ushort writerId = 0)
        {
            var cfg = new DataSetReaderDataType
            {
                Name = $"reader-{writerId}",
                DataSetWriterId = writerId
            };
            return new DataSetReader(
                cfg, NullSink.Instance, NUnitTelemetryContext.Create(), TimeProvider.System);
        }

        private static DataSetReader MakeReaderWithSink(
            ushort writerId,
            ISubscribedDataSetSink sink)
        {
            var cfg = new DataSetReaderDataType
            {
                Name = $"reader-{writerId}",
                DataSetWriterId = writerId
            };
            return new DataSetReader(
                cfg, sink, NUnitTelemetryContext.Create(), TimeProvider.System);
        }

        private static ReaderGroup MakeGroup(ArrayOf<DataSetReader> readers = default)
        {
            return new ReaderGroup(
                new ReaderGroupDataType { Name = "test-group" },
                readers.IsNull ? [] : readers,
                NUnitTelemetryContext.Create());
        }

        private sealed class NullSink : ISubscribedDataSetSink
        {
            public static NullSink Instance { get; } = new();

            public ValueTask WriteAsync(
                IReadOnlyList<DataSetField> fields,
                CancellationToken cancellationToken = default)
            {
                return default;
            }
        }

        private sealed class CountingSink : ISubscribedDataSetSink
        {
            public int CallCount { get; private set; }

            public ValueTask WriteAsync(
                IReadOnlyList<DataSetField> fields,
                CancellationToken cancellationToken = default)
            {
                CallCount++;
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
            {
                throw m_exception;
            }
        }

        private sealed class TrackingScheduler : IPubSubScheduler
        {
            public int ScheduleCallCount { get; private set; }

            public ValueTask<IAsyncDisposable> ScheduleAsync(
                PubSubSchedule schedule,
                Func<CancellationToken, ValueTask> action,
                CancellationToken cancellationToken = default)
            {
                ScheduleCallCount++;
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
