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
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Groups
{
    /// <summary>
    /// Validates the deterministic timeout behaviour of
    /// <see cref="DataSetReaderTimeoutWatcher"/>: when a reader does not
    /// see a dispatch within its <c>MessageReceiveTimeout</c> the watcher
    /// must increment the <c>MessageReceiveTimeouts</c> diagnostics
    /// counter, record a <c>BadTimeout</c> error and fault the reader's
    /// state machine.
    /// </summary>
    /// <remarks>
    /// Covers
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.9.6">
    /// Part 14 §6.2.9.6 DataSetReader status</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.6.3">
    /// §9.1.6.3 ReaderGroup state transitions</see>.
    /// </remarks>
    [TestFixture]
    [TestSpec("6.2.9.6", Summary = "MessageReceiveTimeout fault path")]
    [TestSpec("9.1.6.3")]
    public class DataSetReaderTimeoutWatcherTests
    {
        [Test]
        public async Task PollOnceAsync_FaultsReaderAndIncrementsCounterAsync()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            DataSetReader reader = BuildReader(clock, timeoutMs: 500);
            var diagnostics = new PubSubDiagnostics(
                PubSubDiagnosticsLevel.High, clock);

            await using var watcher = new DataSetReaderTimeoutWatcher(
                [reader],
                new NoOpScheduler(),
                diagnostics,
                NUnitTelemetryContext.Create());

            // No time elapsed — reader stays operational, counter stays at 0.
            await watcher.PollOnceAsync().ConfigureAwait(false);
            Assert.That(reader.State.State, Is.EqualTo(PubSubState.Operational));
            Assert.That(
                diagnostics.Read(PubSubDiagnosticsCounterKind.MessageReceiveTimeouts),
                Is.Zero);

            // Advance the clock past the receive timeout.
            clock.Advance(TimeSpan.FromMilliseconds(750));
            await watcher.PollOnceAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(reader.State.State, Is.EqualTo(PubSubState.Error));
                Assert.That(reader.State.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTimeout));
                Assert.That(
                    diagnostics.Read(PubSubDiagnosticsCounterKind.MessageReceiveTimeouts),
                    Is.EqualTo(1L));
            });

            // Second poll after the reader is already in Error must not
            // re-increment the counter (spec: one fault per timeout).
            await watcher.PollOnceAsync().ConfigureAwait(false);
            Assert.That(
                diagnostics.Read(PubSubDiagnosticsCounterKind.MessageReceiveTimeouts),
                Is.EqualTo(1L));
        }

        [Test]
        public async Task PollOnceAsync_DoesNotFaultBeforeTimeoutAsync()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            DataSetReader reader = BuildReader(clock, timeoutMs: 5000);
            var diagnostics = new PubSubDiagnostics(
                PubSubDiagnosticsLevel.High, clock);

            await using var watcher = new DataSetReaderTimeoutWatcher(
                [reader],
                new NoOpScheduler(),
                diagnostics,
                NUnitTelemetryContext.Create());

            clock.Advance(TimeSpan.FromMilliseconds(250));
            await watcher.PollOnceAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(reader.State.State, Is.EqualTo(PubSubState.Operational));
                Assert.That(
                    diagnostics.Read(PubSubDiagnosticsCounterKind.MessageReceiveTimeouts),
                    Is.Zero);
            });
        }

        private static DataSetReader BuildReader(TimeProvider clock, int timeoutMs)
        {
            var config = new DataSetReaderDataType
            {
                Name = "reader",
                DataSetWriterId = 1,
                WriterGroupId = 1,
                MessageReceiveTimeout = timeoutMs
            };
            var reader = new DataSetReader(
                config,
                NullSink.Instance,
                NUnitTelemetryContext.Create(),
                clock);
            _ = reader.State.TryEnable();
            _ = reader.State.TryMarkOperational();
            return reader;
        }

        private sealed class NullSink : ISubscribedDataSetSink
        {
            public static NullSink Instance { get; } = new();

            public ValueTask WriteAsync(
                IReadOnlyList<DataSetField> fields,
                System.Threading.CancellationToken cancellationToken = default)
            {
                return default;
            }
        }

        private sealed class NoOpScheduler : Opc.Ua.PubSub.Scheduling.IPubSubScheduler
        {
            public ValueTask<IAsyncDisposable> ScheduleAsync(
                Opc.Ua.PubSub.Scheduling.PubSubSchedule schedule,
                Func<System.Threading.CancellationToken, ValueTask> action,
                System.Threading.CancellationToken cancellationToken = default)
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
